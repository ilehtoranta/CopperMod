using System.Runtime.InteropServices;
using Amiga;

namespace CopperMod.Amiga.CopperStart.Layers;

internal enum LayersCallbackReturnFault
{
    None,
    NotPending,
    WrongActor,
    InvalidBoundary,
    InvalidStack,
    InvalidToken,
    InvalidOwner,
    UnexpectedDispatch
}

/// <summary>
/// Private host transport on the caller's guest stack, not an Amiga ABI object.
/// The expected copy and non-reused issuance nonce live in the host owner. This
/// detects a supplied stale packet; it is not protection against arbitrary
/// guest control flow using the current packet at the fixed return gateway.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
internal struct LayersHostCallbackToken
{
    internal const uint ExpectedMagic = 0x4353_4C52; // CSLR
    internal const ushort CurrentVersion = 1;
    internal const ushort Size = 36;

    public uint Magic;
    public ushort Version;
    public ushort Bytes;
    public uint Nonce;
    public APTR LibraryBase;
    public APTR Root;
    public APTR Actor;
    public APTR Continuation;
    public uint RecordGeneration;
    public uint ContinuationToken;
}

/// <summary>The single big-endian codec for the private stack token.</summary>
internal static class LayersHostCallbackTokenCodec
{
    private const int Magic = 0;
    private const int Version = 4;
    private const int Bytes = 6;
    private const int Nonce = 8;
    private const int LibraryBase = 12;
    private const int Root = 16;
    private const int Actor = 20;
    private const int Continuation = 24;
    private const int RecordGeneration = 28;
    private const int ContinuationToken = 32;

    internal static LayersHostCallbackToken Read<T>(ref T memory, APTR address)
        where T : struct, IAmigaGuestMemory => new()
    {
        Magic = memory.ReadUInt32(address, Magic),
        Version = memory.ReadUInt16(address, Version),
        Bytes = memory.ReadUInt16(address, Bytes),
        Nonce = memory.ReadUInt32(address, Nonce),
        LibraryBase = APTR.FromPointer(memory.ReadUInt32(address, LibraryBase)),
        Root = APTR.FromPointer(memory.ReadUInt32(address, Root)),
        Actor = APTR.FromPointer(memory.ReadUInt32(address, Actor)),
        Continuation = APTR.FromPointer(memory.ReadUInt32(address, Continuation)),
        RecordGeneration = memory.ReadUInt32(address, RecordGeneration),
        ContinuationToken = memory.ReadUInt32(address, ContinuationToken)
    };

    internal static void Write<T>(ref T memory, APTR address, LayersHostCallbackToken value)
        where T : struct, IAmigaGuestMemory
    {
        memory.WriteUInt32(address, Magic, value.Magic);
        memory.WriteUInt16(address, Version, value.Version);
        memory.WriteUInt16(address, Bytes, value.Bytes);
        memory.WriteUInt32(address, Nonce, value.Nonce);
        memory.WriteUInt32(address, LibraryBase, value.LibraryBase.Raw);
        memory.WriteUInt32(address, Root, value.Root.Raw);
        memory.WriteUInt32(address, Actor, value.Actor.Raw);
        memory.WriteUInt32(address, Continuation, value.Continuation.Raw);
        memory.WriteUInt32(address, RecordGeneration, value.RecordGeneration);
        memory.WriteUInt32(address, ContinuationToken, value.ContinuationToken);
    }

    internal static bool Same(LayersHostCallbackToken left, LayersHostCallbackToken right) =>
        left.Magic == right.Magic && left.Version == right.Version &&
        left.Bytes == right.Bytes && left.Nonce == right.Nonce &&
        left.LibraryBase == right.LibraryBase && left.Root == right.Root &&
        left.Actor == right.Actor && left.Continuation == right.Continuation &&
        left.RecordGeneration == right.RecordGeneration &&
        left.ContinuationToken == right.ContinuationToken;
}
