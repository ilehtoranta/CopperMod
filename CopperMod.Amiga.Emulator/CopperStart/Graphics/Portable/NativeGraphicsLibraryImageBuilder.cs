using System;
using System.Collections.Generic;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using CopperMod.Amiga.Core;
using CopperMod.Amiga.Firmware;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>
/// Builds the guest data envelope used by a native 68k graphics.library.
/// The builder never invents an implementation address: every public or
/// reserved slot must use an explicitly supplied entry (the fallback is
/// required for the deliberately unimplemented slots).
/// </summary>
internal static class NativeGraphicsLibraryImageBuilder
{
    internal const int VectorStubSize = 6;
    internal const int FirstVectorDisplacement = -6;
    internal const int LastVectorDisplacement = GraphicsLvoCatalog.LastPublicLvo;
    internal const int VectorSlotCount = (-LastVectorDisplacement) / VectorStubSize;
    internal const int VectorTableSize = VectorSlotCount * VectorStubSize;
    internal const int PositiveImageSize = GraphicsLibraryImageLayout.MinimumImageSize;

    private const int ResidentMatchTagOffset = 0x02;
    private const int ResidentEndSkipOffset = 0x06;
    private const int ResidentFlagsOffset = 0x0A;
    private const int ResidentVersionOffset = 0x0B;
    private const int ResidentTypeOffset = 0x0C;
    private const int ResidentPriorityOffset = 0x0D;
    private const int ResidentNameOffset = 0x0E;
    private const int ResidentIdOffset = 0x12;
    private const int ResidentInitOffset = 0x16;
    private const int ResidentNameDataOffset = 0x20;
    private const int ResidentIdDataOffset = 0x34;
    private const int ResidentInitDataOffset = 0x50;
    private const int ResidentFunctionArrayOffset = 0x60;
    private const byte ResidentAutoInit = 0x80;
    private const byte LibraryNodeType = 9;
    private const ushort ResidentMatchWord = 0x4AFC;
    private const uint VectorTerminator = 0xFFFF_FFFF;

    /// <summary>
    /// Builds a fixed-base native image from the current raster-body stream.
    /// The supplied <paramref name="codeAddress"/> is the address at which
    /// the caller will place that stream; this helper turns the relative
    /// offsets returned by <c>NativeGraphicsRasterBodies.BuildCode</c>
    /// into the absolute JMP/function-array targets required by a resident
    /// 68k image.
    /// </summary>
    internal static NativeGraphicsLibraryImage BuildFromRasterBodies(
        uint libraryBase,
        uint residentAddress,
        uint codeAddress,
        uint libraryInitAddress,
        int positiveImageSize = PositiveImageSize,
        GraphicsLibraryImageProfile profile = GraphicsLibraryImageProfile.CompactHost,
        bool includeNativeRuntimeDescriptor = false,
        bool includeNativeMonitorDescriptor = false,
        bool includeNativeMonitorMutation = false)
    {
        ValidateAddress(codeAddress, nameof(codeAddress));
        Dictionary<GraphicsLvo, int> relativeEntryOffsets;
        int relativeFallbackEntryOffset;
        var relativePrivateMutationEntryOffset = 0;
        var nativeCode = includeNativeMonitorMutation
            ? NativeGraphicsRasterBodies.BuildCodeWithMonitorMutation(
                out relativeEntryOffsets,
                out relativeFallbackEntryOffset,
                out relativePrivateMutationEntryOffset,
                defaultMonitorNtsc: profile == GraphicsLibraryImageProfile.NativeNtsc)
            : NativeGraphicsRasterBodies.BuildCode(
                out relativeEntryOffsets,
                out relativeFallbackEntryOffset,
                defaultMonitorNtsc: profile == GraphicsLibraryImageProfile.NativeNtsc);
        var absoluteEntryPoints = new Dictionary<GraphicsLvo, uint>(
            relativeEntryOffsets.Count);
        foreach (var pair in relativeEntryOffsets)
        {
            absoluteEntryPoints[pair.Key] = checked(
                codeAddress + (uint)pair.Value);
        }

        var absoluteFallbackEntry = checked(
            codeAddress + (uint)relativeFallbackEntryOffset);
        IReadOnlyDictionary<int, uint>? privateEntryPoints = null;
        if (includeNativeMonitorMutation)
        {
            privateEntryPoints = new Dictionary<int, uint>
            {
                [GraphicsPrivateLvo.SetDisplayInfoData] = checked(
                    codeAddress + (uint)relativePrivateMutationEntryOffset)
            };
        }
        _ = nativeCode; // The caller owns placement of the generated stream.
        return Build(
            libraryBase,
            residentAddress,
            libraryInitAddress,
            absoluteEntryPoints,
            absoluteFallbackEntry,
            positiveImageSize,
            profile,
            includeNativeRuntimeDescriptor,
            includeNativeMonitorDescriptor,
            privateEntryPoints);
    }

    internal static NativeGraphicsLibraryImage Build(
        uint libraryBase,
        uint residentAddress,
        uint libraryInitAddress,
        IReadOnlyDictionary<GraphicsLvo, uint> entryPoints,
        uint unimplementedEntry,
        int positiveImageSize = PositiveImageSize,
        GraphicsLibraryImageProfile profile = GraphicsLibraryImageProfile.CompactHost,
        bool includeNativeRuntimeDescriptor = false,
        bool includeNativeMonitorDescriptor = false,
        IReadOnlyDictionary<int, uint>? privateEntryPoints = null)
    {
        ArgumentNullException.ThrowIfNull(entryPoints);
        ValidateAddress(libraryBase, nameof(libraryBase));
        ValidateAddress(residentAddress, nameof(residentAddress));
        ValidateAddress(libraryInitAddress, nameof(libraryInitAddress));
        ValidateEntry(unimplementedEntry, nameof(unimplementedEntry));
        if (positiveImageSize < GraphicsLibraryImageLayout.MinimumImageSize)
            throw new ArgumentOutOfRangeException(nameof(positiveImageSize));
        if (libraryBase < VectorTableSize) throw new ArgumentOutOfRangeException(nameof(libraryBase));

        var vectorBase = libraryBase - (uint)VectorTableSize;
        var vectorBytes = new byte[VectorTableSize];
        var functionEntries = new uint[VectorSlotCount];
        for (var slot = 0; slot < VectorSlotCount; slot++)
        {
            var displacement = FirstVectorDisplacement - (slot * VectorStubSize);
            var isPublic = Enum.IsDefined(typeof(GraphicsLvo), displacement);
            var publicEntry = 0u;
            if (isPublic && !entryPoints.TryGetValue((GraphicsLvo)displacement, out publicEntry))
                throw new ArgumentException($"A native entry is required for public graphics LVO {displacement}.", nameof(entryPoints));

            var target = isPublic ? publicEntry : unimplementedEntry;
            if (!isPublic && privateEntryPoints is not null &&
                privateEntryPoints.TryGetValue(displacement, out var privateEntry))
                target = privateEntry;
            ValidateEntry(target, $"entryPoints[{displacement}]");
            functionEntries[slot] = target;

            // The ordinal function array starts at -6, but the physical table
            // precedes LibraryBase: each JMP lives at LibraryBase + its LVO.
            var offset = VectorTableSize + displacement;
            BigEndian.WriteUInt16(vectorBytes, offset, 0x4EF9); // JMP.l target
            BigEndian.WriteUInt32(vectorBytes, offset + 2, target);
        }

        var positiveBytes = GraphicsLibraryImageLayout.CreateGuestImage(
            libraryBase,
            positiveImageSize,
            VectorTableSize,
            positiveImageSize,
            version: 40,
            revision: 68,
            name: "graphics.library",
            idString: "graphics.library 40.68",
            profile: profile,
            includeNativeRuntimeDescriptor: includeNativeRuntimeDescriptor,
            includeNativeMonitorDescriptor: includeNativeMonitorDescriptor);

        var residentBytes = BuildResident(
            residentAddress,
            libraryInitAddress,
            positiveImageSize,
            functionEntries);

        return new NativeGraphicsLibraryImage(
            libraryBase,
            vectorBase,
            vectorBytes,
            positiveBytes,
            residentAddress,
            residentBytes,
            checked(residentAddress + (uint)ResidentFunctionArrayOffset));
    }

    private static byte[] BuildResident(
        uint residentAddress,
        uint libraryInitAddress,
        int positiveSize,
        IReadOnlyList<uint> functionEntries)
    {
        var residentSize = checked(ResidentFunctionArrayOffset + ((functionEntries.Count + 1) * 4));
        var resident = new byte[residentSize];
        BigEndian.WriteUInt16(resident, 0x00, ResidentMatchWord);
        BigEndian.WriteUInt32(resident, ResidentMatchTagOffset, residentAddress);
        BigEndian.WriteUInt32(resident, ResidentEndSkipOffset, checked(residentAddress + (uint)residentSize));
        resident[ResidentFlagsOffset] = ResidentAutoInit;
        resident[ResidentVersionOffset] = 40;
        resident[ResidentTypeOffset] = LibraryNodeType;
        resident[ResidentPriorityOffset] = 0;
        BigEndian.WriteUInt32(resident, ResidentNameOffset, checked(residentAddress + (uint)ResidentNameDataOffset));
        BigEndian.WriteUInt32(resident, ResidentIdOffset, checked(residentAddress + (uint)ResidentIdDataOffset));
        BigEndian.WriteUInt32(resident, ResidentInitOffset, checked(residentAddress + (uint)ResidentInitDataOffset));

        BigEndian.WriteUInt32(resident, ResidentInitDataOffset, (uint)positiveSize);
        BigEndian.WriteUInt32(resident, ResidentInitDataOffset + 4, checked(residentAddress + (uint)ResidentFunctionArrayOffset));
        BigEndian.WriteUInt32(resident, ResidentInitDataOffset + 8, 0);
        BigEndian.WriteUInt32(resident, ResidentInitDataOffset + 12, libraryInitAddress);
        for (var index = 0; index < functionEntries.Count; index++)
            BigEndian.WriteUInt32(resident, ResidentFunctionArrayOffset + (index * 4), functionEntries[index]);
        BigEndian.WriteUInt32(resident, ResidentFunctionArrayOffset + (functionEntries.Count * 4), VectorTerminator);

        WriteAscii(resident, ResidentNameDataOffset, "graphics.library");
        WriteAscii(resident, ResidentIdDataOffset, "graphics.library 40.68");
        return resident;
    }

    private static void ValidateAddress(uint address, string parameterName)
    {
        if (address == 0 || (address & 1) != 0)
            throw new ArgumentException("A native 68k entry/address must be non-zero and word aligned.", parameterName);
    }

    private static void ValidateEntry(uint entry, string parameterName)
        => ValidateAddress(entry, parameterName);

    private static void WriteAscii(Span<byte> destination, int offset, string value)
    {
        for (var index = 0; index < value.Length; index++) destination[offset + index] = (byte)value[index];
        destination[offset + value.Length] = 0;
    }
}

internal sealed class NativeGraphicsLibraryImage
{
    internal NativeGraphicsLibraryImage(
        uint libraryBase,
        uint vectorBase,
        byte[] vectorBytes,
        byte[] positiveBytes,
        uint residentAddress,
        byte[] residentBytes,
        uint functionArrayAddress)
    {
        LibraryBase = libraryBase;
        VectorBase = vectorBase;
        VectorBytes = vectorBytes;
        PositiveBytes = positiveBytes;
        ResidentAddress = residentAddress;
        ResidentBytes = residentBytes;
        FunctionArrayAddress = functionArrayAddress;
    }

    internal uint LibraryBase { get; }
    internal uint VectorBase { get; }
    internal byte[] VectorBytes { get; }
    internal byte[] PositiveBytes { get; }
    internal uint ResidentAddress { get; }
    internal byte[] ResidentBytes { get; }
    internal uint FunctionArrayAddress { get; }
}
