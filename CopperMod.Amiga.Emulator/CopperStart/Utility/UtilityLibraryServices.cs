using System;
using System.Collections.Generic;
using Amiga;
using Copper68k;
using PortableUtility = CopperStart.Utility;

namespace CopperMod.Amiga.CopperStart.Utility;

/// <summary>Host gateways for the shared, guest-resident portable utility.library core.</summary>
internal sealed class UtilityLibraryServices : IDisposable
{
	private const uint HookContinuationAddress = 0x00F08A00;
	private readonly CopperStartUtilityContext _context;
	private readonly List<(uint Address, uint Token)> _gateways = new();
	private uint _portableState;

	public UtilityLibraryServices(CopperStartUtilityContext context) => _context = context ?? throw new ArgumentNullException(nameof(context));
	public uint LibraryBase { get; private set; }
	public bool IsInstalled => _gateways.Count != 0;
	internal int GatewayRegistrationCountForTests() => _gateways.Count;

	public bool TryInstall(uint execBase)
	{
		if (IsInstalled || execBase == 0 || !_context.Memory.IsMapped(execBase + ExecLayout.ExecBase.LibraryList, (int)global::Amiga.List.Size)) return IsInstalled;
		LibraryBase = FindLibrary(execBase + ExecLayout.ExecBase.LibraryList); if (LibraryBase == 0 || LibraryBase < 96 || !_context.Memory.IsMapped(LibraryBase - 96, 96)) return false;
		_portableState = _context.Allocate((int)PortableUtility.UtilityCore.StateSize, (uint)(global::Amiga.Exec.MemoryFlags.Public | global::Amiga.Exec.MemoryFlags.Clear)); if (_portableState == 0) { LibraryBase = 0; return false; }
		var initial = new UtilityHostPlatform(_context, null); PortableUtility.UtilityCore.Initialize(ref initial, APTR.FromPointer(_portableState));
		Register(UtilityLvo.FindTagItem, s => With(s, (ref UtilityHostPlatform p) => s.D[0] = PortableUtility.UtilityCore.FindTagItem(ref p, s.D[0], Ptr(s.A[0])).Raw));
		Register(UtilityLvo.GetTagData, s => With(s, (ref UtilityHostPlatform p) => s.D[0] = PortableUtility.UtilityCore.GetTagData(ref p, s.D[0], s.D[1], Ptr(s.A[0]))));
		Register(UtilityLvo.PackBoolTags, s => With(s, (ref UtilityHostPlatform p) => s.D[0] = PortableUtility.UtilityCore.PackBoolTags(ref p, s.D[0], Ptr(s.A[0]), Ptr(s.A[1]))));
		Register(UtilityLvo.NextTagItem, s => With(s, (ref UtilityHostPlatform p) => s.D[0] = PortableUtility.UtilityCore.NextTagItem(ref p, Ptr(s.A[0])).Raw));
		Register(UtilityLvo.FilterTagChanges, s => With(s, (ref UtilityHostPlatform p) => { PortableUtility.UtilityCore.FilterTagChanges(ref p, Ptr(s.A[0]), Ptr(s.A[1]), s.D[0] != 0); s.D[0] = 0; }));
		Register(UtilityLvo.MapTags, s => With(s, (ref UtilityHostPlatform p) => s.D[0] = PortableUtility.UtilityCore.MapTags(ref p, Ptr(s.A[0]), Ptr(s.A[1]), s.D[0] != 0)));
		Register(UtilityLvo.AllocateTagItems, s => With(s, (ref UtilityHostPlatform p) => s.D[0] = PortableUtility.UtilityCore.AllocateTagItems(ref p, s.D[0]).Raw));
		Register(UtilityLvo.CloneTagItems, s => With(s, (ref UtilityHostPlatform p) => s.D[0] = PortableUtility.UtilityCore.CloneTagItems(ref p, Ptr(s.A[0])).Raw));
		Register(UtilityLvo.FreeTagItems, s => With(s, (ref UtilityHostPlatform p) => PortableUtility.UtilityCore.FreeTagItems(ref p, Ptr(s.A[0]))));
		Register(UtilityLvo.RefreshTagItemClones, s => With(s, (ref UtilityHostPlatform p) => { PortableUtility.UtilityCore.RefreshTagItemClones(ref p, Ptr(s.A[0]), Ptr(s.A[1])); s.D[0] = 0; }));
		Register(UtilityLvo.TagInArray, s => With(s, (ref UtilityHostPlatform p) => s.D[0] = PortableUtility.UtilityCore.TagInArray(ref p, s.D[0], Ptr(s.A[0])) ? 1u : 0u));
		Register(UtilityLvo.FilterTagItems, s => With(s, (ref UtilityHostPlatform p) => s.D[0] = PortableUtility.UtilityCore.FilterTagItems(ref p, Ptr(s.A[0]), Ptr(s.A[1]), s.D[0] != 0)));
		Register(UtilityLvo.CallHookPkt, s => With(s, (ref UtilityHostPlatform p) => { if (!PortableUtility.UtilityCore.CallHookPkt(ref p, Ptr(s.A[0]), Ptr(s.A[1]), Ptr(s.A[2]))) s.D[0] = 0; }));
		Register(UtilityLvo.Amiga2Date, s => With(s, (ref UtilityHostPlatform p) => PortableUtility.UtilityCore.Amiga2Date(ref p, s.D[0], Ptr(s.A[0]))));
		Register(UtilityLvo.Date2Amiga, s => With(s, (ref UtilityHostPlatform p) => s.D[0] = PortableUtility.UtilityCore.Date2Amiga(ref p, Ptr(s.A[0]))));
		Register(UtilityLvo.CheckDate, s => With(s, (ref UtilityHostPlatform p) => s.D[0] = PortableUtility.UtilityCore.Date2Amiga(ref p, Ptr(s.A[0]))));
		Register(UtilityLvo.SMult32, s => s.D[0] = unchecked((uint)((int)s.D[0] * (int)s.D[1])));
		Register(UtilityLvo.UMult32, s => s.D[0] = unchecked(s.D[0] * s.D[1]));
		Register(UtilityLvo.SDivMod32, SignedDivide); Register(UtilityLvo.UDivMod32, UnsignedDivide);
		Register(UtilityLvo.Stricmp, s => With(s, (ref UtilityHostPlatform p) => s.D[0] = unchecked((uint)PortableUtility.UtilityCore.Stricmp(ref p, Ptr(s.A[0]), Ptr(s.A[1]), uint.MaxValue))));
		Register(UtilityLvo.Strnicmp, s => With(s, (ref UtilityHostPlatform p) => s.D[0] = unchecked((uint)PortableUtility.UtilityCore.Stricmp(ref p, Ptr(s.A[0]), Ptr(s.A[1]), s.D[0]))));
		Register(UtilityLvo.ToUpper, s => s.D[0] = PortableUtility.UtilityCore.ToUpper((byte)s.D[0])); Register(UtilityLvo.ToLower, s => s.D[0] = PortableUtility.UtilityCore.ToLower((byte)s.D[0]));
		Register(UtilityLvo.ApplyTagChanges, s => With(s, (ref UtilityHostPlatform p) => { PortableUtility.UtilityCore.ApplyTagChanges(ref p, Ptr(s.A[0]), Ptr(s.A[1])); s.D[0] = 0; }));
		Register(UtilityLvo.SMult64, SignedMultiply64); Register(UtilityLvo.UMult64, UnsignedMultiply64);
		Register(UtilityLvo.PackStructureTags, s => With(s, (ref UtilityHostPlatform p) => s.D[0] = PortableUtility.UtilityCore.PackStructureTags(ref p, Ptr(s.A[0]), Ptr(s.A[1]), Ptr(s.A[2]), false)));
		Register(UtilityLvo.UnpackStructureTags, s => With(s, (ref UtilityHostPlatform p) => s.D[0] = PortableUtility.UtilityCore.PackStructureTags(ref p, Ptr(s.A[0]), Ptr(s.A[1]), Ptr(s.A[2]), true)));
		Register(UtilityLvo.AddNamedObject, s => With(s, (ref UtilityHostPlatform p) => s.D[0] = PortableUtility.UtilityCore.AddNamedObject(ref p, Ptr(_portableState), Ptr(s.A[0]), Ptr(s.A[1])) ? 1u : 0u));
		Register(UtilityLvo.AllocNamedObjectA, s => With(s, (ref UtilityHostPlatform p) => s.D[0] = PortableUtility.UtilityCore.AllocNamedObject(ref p, Ptr(s.A[0]), Ptr(s.A[1])).Raw));
		Register(UtilityLvo.AttemptRemNamedObject, s => With(s, (ref UtilityHostPlatform p) => s.D[0] = PortableUtility.UtilityCore.AttemptRemNamedObject(ref p, Ptr(_portableState), Ptr(s.A[0])) ? 1u : 0u));
		Register(UtilityLvo.FindNamedObject, s => With(s, (ref UtilityHostPlatform p) => s.D[0] = PortableUtility.UtilityCore.FindNamedObject(ref p, Ptr(_portableState), Ptr(s.A[0]), Ptr(s.A[1]), Ptr(s.A[2])).Raw));
		Register(UtilityLvo.FreeNamedObject, s => With(s, (ref UtilityHostPlatform p) => PortableUtility.UtilityCore.FreeNamedObject(ref p, Ptr(s.A[0]))));
		Register(UtilityLvo.NamedObjectName, s => With(s, (ref UtilityHostPlatform p) => s.D[0] = PortableUtility.UtilityCore.NamedObjectName(ref p, Ptr(s.A[0])).Raw));
		Register(UtilityLvo.ReleaseNamedObject, s => With(s, (ref UtilityHostPlatform p) => PortableUtility.UtilityCore.ReleaseNamedObject(ref p, Ptr(_portableState), Ptr(s.A[0]))));
		Register(UtilityLvo.RemNamedObject, s => With(s, (ref UtilityHostPlatform p) => PortableUtility.UtilityCore.RemNamedObject(ref p, Ptr(_portableState), Ptr(s.A[0]), Ptr(s.A[1]))));
		Register(UtilityLvo.GetUniqueID, s => With(s, (ref UtilityHostPlatform p) => s.D[0] = PortableUtility.UtilityCore.GetUniqueId(ref p, Ptr(_portableState))));
		RegisterAddress(HookContinuationAddress, _ => { }); return true;
	}

	public void Reset() { for (var i = _gateways.Count - 1; i >= 0; i--) _context.Bus.RemoveHostGateway(_gateways[i].Address, _gateways[i].Token); _gateways.Clear(); if (_portableState != 0) _context.Free(_portableState, (int)PortableUtility.UtilityCore.StateSize); _portableState = 0; LibraryBase = 0; }
	public void Dispose() => Reset();
	private delegate void PlatformAction(ref UtilityHostPlatform platform);
	private void With(M68kCpuState state, PlatformAction action) { var p = new UtilityHostPlatform(_context, state); action(ref p); }
	private void Register(int lvo, Action<M68kCpuState> handler) => RegisterAddress(unchecked((uint)((int)LibraryBase + lvo)), handler);
	private void RegisterAddress(uint address, Action<M68kCpuState> handler) => _gateways.Add((address, _context.Bus.RegisterHostGateway(address, handler)));
	private static APTR Ptr(uint value) => APTR.FromPointer(value);
	private static void SignedDivide(M68kCpuState s) { var a = (int)s.D[0]; var b = (int)s.D[1]; if (b == 0) { s.D[0] = 0; s.D[1] = unchecked((uint)a); } else { s.D[0] = unchecked((uint)((long)a / b)); s.D[1] = unchecked((uint)((long)a % b)); } }
	private static void UnsignedDivide(M68kCpuState s) { var a = s.D[0]; var b = s.D[1]; if (b == 0) { s.D[0] = 0; s.D[1] = a; } else { s.D[0] = a / b; s.D[1] = a % b; } }
	private static void SignedMultiply64(M68kCpuState s) { var v = (long)(int)s.D[0] * (int)s.D[1]; s.D[0] = unchecked((uint)(v >> 32)); s.D[1] = unchecked((uint)v); }
	private static void UnsignedMultiply64(M68kCpuState s) { var v = (ulong)s.D[0] * s.D[1]; s.D[0] = (uint)(v >> 32); s.D[1] = (uint)v; }
	private uint FindLibrary(uint list) { var tail = list + ExecLayout.List.Tail; for (var node = _context.Memory.ReadLong(list); node != 0 && node != tail && _context.Memory.IsMapped(node, ExecLayout.Node.Name + 4); node = _context.Memory.ReadLong(node)) { var name = _context.Memory.ReadLong(node + ExecLayout.Node.Name); if (EqualsAscii(name, "utility.library")) return node; } return 0; }
	private bool EqualsAscii(uint at, string value) { if (at == 0) return false; for (var i = 0; i <= value.Length; i++) { if (!_context.Memory.IsMapped(at + (uint)i, 1)) return false; var actual = _context.Memory.ReadByte(at + (uint)i); var expected = i == value.Length ? (byte)0 : (byte)value[i]; if (actual is >= (byte)'A' and <= (byte)'Z') actual += 32; if (actual != expected) return false; } return true; }
}

internal struct UtilityHostPlatform : PortableUtility.IUtilityPlatform
{
	private readonly CopperStartUtilityContext _context; private readonly M68kCpuState? _state;
	public UtilityHostPlatform(CopperStartUtilityContext context, M68kCpuState? state) { _context = context; _state = state; }
	public byte ReadUInt8(APTR a, int o = 0) => _context.Memory.ReadByte(a.Raw + (uint)o); public ushort ReadUInt16(APTR a, int o = 0) => _context.Memory.ReadWord(a.Raw + (uint)o); public uint ReadUInt32(APTR a, int o = 0) => _context.Memory.ReadLong(a.Raw + (uint)o);
	public void WriteUInt8(APTR a, int o, byte v) => _context.Memory.WriteByte(a.Raw + (uint)o, v); public void WriteUInt16(APTR a, int o, ushort v) => _context.Memory.WriteWord(a.Raw + (uint)o, v); public void WriteUInt32(APTR a, int o, uint v) => _context.Memory.WriteLong(a.Raw + (uint)o, v);
	public void Clear(APTR a, uint n) { for (uint i = 0; i < n; i++) _context.Memory.WriteByte(a.Raw + i, 0); } public void Copy(APTR s, APTR d, uint n) { if (d.Raw > s.Raw && d.Raw < s.Raw + n) for (var i = n; i != 0; i--) WriteUInt8(d, (int)(i - 1), ReadUInt8(s, (int)(i - 1))); else for (uint i = 0; i < n; i++) WriteUInt8(d, (int)i, ReadUInt8(s, (int)i)); }
	public bool IsMapped(APTR a, uint n) => n <= int.MaxValue && _context.Memory.IsMapped(a.Raw, (int)n); public APTR Allocate(uint n, global::Amiga.Exec.MemoryFlags f) => APTR.FromPointer(n <= int.MaxValue ? _context.Allocate((int)n, (uint)f) : 0); public void Free(APTR a, uint n) { if (n <= int.MaxValue) _context.Free(a.Raw, (int)n); }
	public bool StartHook(APTR hook, APTR entry, APTR message, APTR target) { if (_state is null || _context.StartGuestSubroutine is null) return false; _state.A[0] = hook.Raw; _state.A[1] = message.Raw; _state.A[2] = target.Raw; _context.StartGuestSubroutine(_state, entry.Raw, 0x00F08A00); return true; }
	public void ReplyMessage(APTR message) { if (_state is not null) _context.ReplyMessage?.Invoke(message.Raw); }
}
