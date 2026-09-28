using System;
using System.Collections.Generic;
using CopperMod.Amiga.Firmware;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>
/// Emits the smallest relocatable Amiga HUNK envelope that can carry the
/// native graphics image contract.  The first segment contains the negative
/// vector table, the positive GfxBase image, and caller-supplied native code;
/// the second segment contains the AUTOINIT resident/function array.  The
/// bytes stored in absolute guest fields are segment-relative until a loader
/// applies the HUNK_RELOC32 records.
/// </summary>
internal static class NativeGraphicsLibraryHunkBuilder
{
    private const uint HunkCode = 0x0000_03E9;
    private const uint HunkData = 0x0000_03EA;
    private const uint HunkReloc32 = 0x0000_03EC;
    private const uint HunkEnd = 0x0000_03F2;
    private const uint HunkHeader = 0x0000_03F3;
    private const int ResidentMatchTagOffset = 0x02;
    private const int ResidentEndSkipOffset = 0x06;
    private const int ResidentFlagsOffset = 0x0A;
    private const int ResidentVersionOffset = 0x0B;
    private const int ResidentTypeOffset = 0x0C;
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
    /// Builds the relocatable native graphics.library image from the current
    /// CopperSharp68k-compatible raster bodies.  Keeping this composition in
    /// the HUNK builder makes the native-ROM path use the same public vector
    /// map as the direct AccurateM68000 fixtures; callers do not have to copy
    /// or reconstruct the entry-offset dictionary by hand.
    /// </summary>
    internal static NativeGraphicsLibraryHunkImage BuildFromRasterBodies(
        int positiveImageSize = NativeGraphicsLibraryImageBuilder.PositiveImageSize,
        GraphicsLibraryImageProfile profile = GraphicsLibraryImageProfile.CompactHost,
        bool includeNativeRuntimeDescriptor = false,
        bool initializeAllocatedImage = false,
        bool publishNativeMonitorDatabase = false,
        bool supportsEcsDisplay = false,
        bool includeNativeMonitorDescriptor = false,
        bool publishNativeMonitorSpec = false,
        bool includeNativeMonitorMutation = false)
    {
        Dictionary<GraphicsLvo, int> entryOffsets;
        int fallbackEntryOffset;
        int privateMutationEntryOffset = 0;
        var nativeCode = includeNativeMonitorMutation
            ? NativeGraphicsRasterBodies.BuildCodeWithMonitorMutation(
                out entryOffsets, out fallbackEntryOffset,
                out privateMutationEntryOffset,
                defaultMonitorNtsc: profile == GraphicsLibraryImageProfile.NativeNtsc)
            : NativeGraphicsRasterBodies.BuildCode(
                out entryOffsets, out fallbackEntryOffset,
                defaultMonitorNtsc: profile == GraphicsLibraryImageProfile.NativeNtsc);
        IReadOnlyDictionary<int, int>? privateEntryOffsets = includeNativeMonitorMutation
            ? new Dictionary<int, int>
            {
                [GraphicsPrivateLvo.SetDisplayInfoData] = privateMutationEntryOffset
            }
            : null;
        return Build(
            nativeCode,
            entryOffsets,
            fallbackEntryOffset,
            positiveImageSize,
            profile,
            includeNativeRuntimeDescriptor,
            initializeAllocatedImage,
            publishNativeMonitorDatabase,
            supportsEcsDisplay,
            includeNativeMonitorDescriptor,
            publishNativeMonitorSpec,
            privateEntryOffsets,
            includeNativeMonitorMutation);
    }

    internal static NativeGraphicsLibraryHunkImage Build(
        ReadOnlySpan<byte> nativeCode,
        int positiveImageSize = NativeGraphicsLibraryImageBuilder.PositiveImageSize,
        GraphicsLibraryImageProfile profile = GraphicsLibraryImageProfile.CompactHost,
        bool includeNativeRuntimeDescriptor = false,
        bool initializeAllocatedImage = false,
        bool publishNativeMonitorDatabase = false,
        bool supportsEcsDisplay = false,
        bool includeNativeMonitorDescriptor = false,
        bool publishNativeMonitorSpec = false,
        IReadOnlyDictionary<int, int>? privateEntryOffsets = null,
        bool includeNativeMonitorMutation = false)
    {
        var entryOffsets = new Dictionary<GraphicsLvo, int>(GraphicsLvoCatalog.PublicVectorCount);
        foreach (var vector in Enum.GetValues<GraphicsLvo>())
            entryOffsets[vector] = 0;

        return Build(
            nativeCode,
            entryOffsets,
            fallbackEntryOffset: 0,
            positiveImageSize: positiveImageSize,
            profile: profile,
            includeNativeRuntimeDescriptor: includeNativeRuntimeDescriptor,
            initializeAllocatedImage: initializeAllocatedImage,
            publishNativeMonitorDatabase: publishNativeMonitorDatabase,
            supportsEcsDisplay: supportsEcsDisplay,
            includeNativeMonitorDescriptor: includeNativeMonitorDescriptor,
            publishNativeMonitorSpec: publishNativeMonitorSpec,
            privateEntryOffsets: privateEntryOffsets,
            includeNativeMonitorMutation: includeNativeMonitorMutation);
    }

    /// <summary>
    /// Builds a relocatable native library whose public vector stubs and
    /// AUTOINIT function array point at explicitly supplied offsets within the
    /// caller's native code stream.  Reserved/private vector slots use the
    /// explicit fallback offset.  Offsets are relative to the first byte of
    /// <paramref name="nativeCode"/> and are relocated with the code segment.
    /// </summary>
    internal static NativeGraphicsLibraryHunkImage Build(
        ReadOnlySpan<byte> nativeCode,
        IReadOnlyDictionary<GraphicsLvo, int> entryOffsets,
        int fallbackEntryOffset,
        int positiveImageSize = NativeGraphicsLibraryImageBuilder.PositiveImageSize,
        GraphicsLibraryImageProfile profile = GraphicsLibraryImageProfile.CompactHost,
        bool includeNativeRuntimeDescriptor = false,
        bool initializeAllocatedImage = false,
        bool publishNativeMonitorDatabase = false,
        bool supportsEcsDisplay = false,
        bool includeNativeMonitorDescriptor = false,
        bool publishNativeMonitorSpec = false,
        IReadOnlyDictionary<int, int>? privateEntryOffsets = null,
        bool includeNativeMonitorMutation = false)
    {
        ArgumentNullException.ThrowIfNull(entryOffsets);
        if (nativeCode.IsEmpty || (nativeCode.Length & 1) != 0)
            throw new ArgumentException("Native HUNK code must be a non-empty even-byte stream.", nameof(nativeCode));
        if (positiveImageSize < GraphicsLibraryImageLayout.MinimumImageSize)
            throw new ArgumentOutOfRangeException(nameof(positiveImageSize));
        if (publishNativeMonitorDatabase && (!initializeAllocatedImage || !includeNativeRuntimeDescriptor))
            throw new ArgumentException("Native monitor publication requires allocated-image initialization and the runtime descriptor.",
                nameof(publishNativeMonitorDatabase));
        if (publishNativeMonitorSpec && (!publishNativeMonitorDatabase || !includeNativeMonitorDescriptor))
            throw new ArgumentException("Native MonitorSpec publication requires monitor database publication and the monitor descriptor.",
                nameof(publishNativeMonitorSpec));
        if (includeNativeMonitorMutation && !publishNativeMonitorDatabase)
            throw new ArgumentException("Native monitor mutation requires native monitor database publication.",
                nameof(includeNativeMonitorMutation));
        if (includeNativeMonitorMutation &&
            (privateEntryOffsets is null ||
             !privateEntryOffsets.ContainsKey(GraphicsPrivateLvo.SetDisplayInfoData)))
            throw new ArgumentException(
                "Native monitor mutation requires an explicit private -750 entry offset.",
                nameof(privateEntryOffsets));

        ValidateEntryOffset(fallbackEntryOffset, nativeCode.Length, nameof(fallbackEntryOffset));
        foreach (var vector in Enum.GetValues<GraphicsLvo>())
        {
            if (!entryOffsets.TryGetValue(vector, out var offset))
                throw new ArgumentException(
                    $"A native entry offset is required for public graphics LVO {(int)vector}.",
                    nameof(entryOffsets));

            ValidateEntryOffset(offset, nativeCode.Length, $"entryOffsets[{vector}]");
        }

        var codeOffset = Align4(NativeGraphicsLibraryImageBuilder.VectorTableSize +
            positiveImageSize);
        var initializer = publishNativeMonitorSpec
            ? NativeGraphicsLibraryInitializer.BuildWithNativeMonitorSpec(positiveImageSize, profile,
                supportsEcsDisplay, releaseLibraryOnFailure: true,
                includeNativeMonitorMutation: includeNativeMonitorMutation)
            : publishNativeMonitorDatabase
            ? NativeGraphicsLibraryInitializer.BuildWithNativeMonitorState(positiveImageSize, profile,
                supportsEcsDisplay, releaseLibraryOnFailure: true,
                includeNativeMonitorDescriptor: includeNativeMonitorDescriptor,
                includeNativeMonitorMutation: includeNativeMonitorMutation)
            : initializeAllocatedImage
                ? NativeGraphicsLibraryInitializer.Build(positiveImageSize, profile, includeNativeRuntimeDescriptor,
                    includeNativeMonitorDescriptor)
                : Array.Empty<byte>();
        var initializerOffset = checked(codeOffset + nativeCode.Length);
        var codeSegmentSize = Align4(checked(initializerOffset + initializer.Length));
        var residentSize = Align4(ResidentFunctionArrayOffset +
            ((NativeGraphicsLibraryImageBuilder.VectorSlotCount + 1) * 4));
        var codeSegment = new byte[codeSegmentSize];
        var residentSegment = new byte[residentSize];
        var entryOffsetsBySlot = BuildEntryOffsetsBySlot(
            entryOffsets,
            fallbackEntryOffset,
            privateEntryOffsets);

        WriteVectorTable(codeSegment, codeOffset, entryOffsetsBySlot);
        var positive = GraphicsLibraryImageLayout.CreateGuestImage(
            libraryBase: (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize,
            imageLength: positiveImageSize,
            reportedNegativeSize: NativeGraphicsLibraryImageBuilder.VectorTableSize,
            reportedPositiveSize: positiveImageSize,
            version: 40,
            revision: 68,
            name: "graphics.library",
            idString: "graphics.library 40.68",
            profile: profile,
            includeNativeRuntimeDescriptor: includeNativeRuntimeDescriptor,
            includeNativeMonitorDescriptor: includeNativeMonitorDescriptor);
        positive.CopyTo(codeSegment.AsSpan(NativeGraphicsLibraryImageBuilder.VectorTableSize));
        nativeCode.CopyTo(codeSegment.AsSpan(codeOffset));
        initializer.CopyTo(codeSegment.AsSpan(initializerOffset));

        WriteResident(residentSegment, codeOffset,
            initializeAllocatedImage ? initializerOffset : codeOffset,
            positiveImageSize, entryOffsetsBySlot);

        var relocations = new List<RelocationGroup>
        {
            new(0, 0, BuildCodeSegmentRelocations(positiveImageSize)),
            new(1, 1, BuildResidentSelfRelocations()),
            new(1, 0, BuildResidentCodeRelocations())
        };
        return new NativeGraphicsLibraryHunkImage(
            BuildHunk(codeSegment, residentSegment, relocations),
            NativeGraphicsLibraryImageBuilder.VectorTableSize,
            codeOffset,
            residentSize);
    }

    private static int[] BuildEntryOffsetsBySlot(
        IReadOnlyDictionary<GraphicsLvo, int> entryOffsets,
        int fallbackEntryOffset,
        IReadOnlyDictionary<int, int>? privateEntryOffsets)
    {
        var offsets = new int[NativeGraphicsLibraryImageBuilder.VectorSlotCount];
        for (var slot = 0; slot < offsets.Length; slot++)
        {
            var displacement = NativeGraphicsLibraryImageBuilder.FirstVectorDisplacement -
                (slot * NativeGraphicsLibraryImageBuilder.VectorStubSize);
            offsets[slot] = Enum.IsDefined(typeof(GraphicsLvo), displacement)
                ? entryOffsets[(GraphicsLvo)displacement]
                : privateEntryOffsets is not null && privateEntryOffsets.TryGetValue(displacement, out var privateOffset)
                    ? privateOffset
                    : fallbackEntryOffset;
        }

        return offsets;
    }

    private static void WriteVectorTable(Span<byte> destination, int codeOffset, IReadOnlyList<int> entryOffsets)
    {
        for (var slot = 0; slot < NativeGraphicsLibraryImageBuilder.VectorSlotCount; slot++)
        {
            // GfxBase follows the negative table; ordinal slot zero (-6) is
            // therefore the last physical JMP, not the first.
            var offset = NativeGraphicsLibraryImageBuilder.VectorTableSize -
                ((slot + 1) * NativeGraphicsLibraryImageBuilder.VectorStubSize);
            BigEndian.WriteUInt16(destination, offset, 0x4EF9); // JMP.L native entry
            BigEndian.WriteUInt32(destination, offset + 2, checked((uint)(codeOffset + entryOffsets[slot])));
        }
    }

    private static void WriteResident(
        Span<byte> resident,
        int codeOffset,
        int initializerOffset,
        int positiveImageSize,
        IReadOnlyList<int> entryOffsets)
    {
        BigEndian.WriteUInt16(resident, 0x00, ResidentMatchWord);
        BigEndian.WriteUInt32(resident, ResidentMatchTagOffset, 0);
        BigEndian.WriteUInt32(resident, ResidentEndSkipOffset, (uint)resident.Length);
        resident[ResidentFlagsOffset] = ResidentAutoInit;
        resident[ResidentVersionOffset] = 40;
        resident[ResidentTypeOffset] = LibraryNodeType;
        BigEndian.WriteUInt32(resident, ResidentNameOffset, ResidentNameDataOffset);
        BigEndian.WriteUInt32(resident, ResidentIdOffset, ResidentIdDataOffset);
        BigEndian.WriteUInt32(resident, ResidentInitOffset, ResidentInitDataOffset);

        BigEndian.WriteUInt32(resident, ResidentInitDataOffset, (uint)positiveImageSize);
        BigEndian.WriteUInt32(resident, ResidentInitDataOffset + 4, ResidentFunctionArrayOffset);
        BigEndian.WriteUInt32(resident, ResidentInitDataOffset + 8, 0);
        BigEndian.WriteUInt32(resident, ResidentInitDataOffset + 12, (uint)initializerOffset);
        for (var index = 0; index < NativeGraphicsLibraryImageBuilder.VectorSlotCount; index++)
        {
            BigEndian.WriteUInt32(
                resident,
                ResidentFunctionArrayOffset + (index * 4),
                checked((uint)(codeOffset + entryOffsets[index])));
        }
        BigEndian.WriteUInt32(
            resident,
            ResidentFunctionArrayOffset + (NativeGraphicsLibraryImageBuilder.VectorSlotCount * 4),
            VectorTerminator);

        WriteAscii(resident, ResidentNameDataOffset, "graphics.library");
        WriteAscii(resident, ResidentIdDataOffset, "graphics.library 40.68");
    }

    private static IReadOnlyList<uint> BuildCodeSegmentRelocations(int positiveImageSize)
    {
        var offsets = new List<uint>(NativeGraphicsLibraryImageBuilder.VectorSlotCount + 6);
        for (var slot = 0; slot < NativeGraphicsLibraryImageBuilder.VectorSlotCount; slot++)
            offsets.Add((uint)(slot * NativeGraphicsLibraryImageBuilder.VectorStubSize + 2));

        var positive = NativeGraphicsLibraryImageBuilder.VectorTableSize;
        offsets.Add((uint)(positive + 0x0A));
        offsets.Add((uint)(positive + 0x18));
        offsets.Add((uint)(positive + GraphicsLibraryImageLayout.GfxBaseTextFontsHead));
        offsets.Add((uint)(positive + GraphicsLibraryImageLayout.GfxBaseTextFontsTailPred));
        if (positiveImageSize >= GraphicsLibraryImageLayout.GfxBaseNativeSize)
        {
            offsets.Add((uint)(positive + GraphicsLibraryImageLayout.GfxBaseMonitorListHead));
            offsets.Add((uint)(positive + GraphicsLibraryImageLayout.GfxBaseMonitorListTailPred));
        }
        return offsets;
    }

    private static IReadOnlyList<uint> BuildResidentSelfRelocations()
        =>
        [
            ResidentMatchTagOffset,
            ResidentEndSkipOffset,
            ResidentNameOffset,
            ResidentIdOffset,
            ResidentInitOffset,
            ResidentInitDataOffset + 4
        ];

    private static IReadOnlyList<uint> BuildResidentCodeRelocations()
    {
        var offsets = new List<uint>(NativeGraphicsLibraryImageBuilder.VectorSlotCount + 1)
        {
            ResidentInitDataOffset + 12
        };
        for (var index = 0; index < NativeGraphicsLibraryImageBuilder.VectorSlotCount; index++)
            offsets.Add((uint)(ResidentFunctionArrayOffset + (index * 4)));
        return offsets;
    }

    private static byte[] BuildHunk(
        ReadOnlySpan<byte> codeSegment,
        ReadOnlySpan<byte> residentSegment,
        IReadOnlyList<RelocationGroup> relocations)
    {
        var output = new List<byte>(
            64 + codeSegment.Length + residentSegment.Length + (relocations.Count * 12));
        WriteUInt32(output, HunkHeader);
        WriteUInt32(output, 0); // no resident-library names
        WriteUInt32(output, 2); // table size
        WriteUInt32(output, 0); // first hunk
        WriteUInt32(output, 1); // last hunk
        WriteUInt32(output, (uint)(codeSegment.Length / 4));
        WriteUInt32(output, (uint)(residentSegment.Length / 4));

        WriteSegment(output, HunkCode, codeSegment, relocations, sourceSegment: 0);
        WriteSegment(output, HunkData, residentSegment, relocations, sourceSegment: 1);
        return output.ToArray();
    }

    private static void WriteSegment(
        List<byte> output,
        uint section,
        ReadOnlySpan<byte> bytes,
        IReadOnlyList<RelocationGroup> relocations,
        int sourceSegment)
    {
        WriteUInt32(output, section);
        WriteUInt32(output, (uint)(bytes.Length / 4));
        output.AddRange(bytes.ToArray());

        WriteUInt32(output, HunkReloc32);
        foreach (var group in relocations)
        {
            if (group.SourceSegment != sourceSegment || group.Offsets.Count == 0)
                continue;

            WriteUInt32(output, (uint)group.Offsets.Count);
            WriteUInt32(output, group.TargetSegment);
            foreach (var offset in group.Offsets)
                WriteUInt32(output, offset);
        }

        WriteUInt32(output, 0); // end of relocation groups
        WriteUInt32(output, HunkEnd);
    }

    private static int Align4(int value)
        => checked((value + 3) & ~3);

    private static void ValidateEntryOffset(int offset, int nativeCodeLength, string parameterName)
    {
        if (offset < 0 || (offset & 1) != 0 || offset > nativeCodeLength - 2)
            throw new ArgumentException(
                "A native HUNK entry offset must be even and point inside the native code stream.",
                parameterName);
    }

    private static void WriteUInt32(List<byte> destination, uint value)
    {
        destination.Add((byte)(value >> 24));
        destination.Add((byte)(value >> 16));
        destination.Add((byte)(value >> 8));
        destination.Add((byte)value);
    }

    private static void WriteAscii(Span<byte> destination, int offset, string value)
    {
        for (var index = 0; index < value.Length; index++)
            destination[offset + index] = (byte)value[index];
        destination[offset + value.Length] = 0;
    }

    private readonly record struct RelocationGroup(
        int SourceSegment,
        uint TargetSegment,
        IReadOnlyList<uint> Offsets);
}

internal sealed class NativeGraphicsLibraryHunkImage
{
    internal NativeGraphicsLibraryHunkImage(
        byte[] bytes,
        int vectorOffset,
        int nativeCodeOffset,
        int residentSegmentSize)
    {
        Bytes = bytes;
        VectorOffset = vectorOffset;
        NativeCodeOffset = nativeCodeOffset;
        ResidentSegmentSize = residentSegmentSize;
    }

    internal byte[] Bytes { get; }
    internal int VectorOffset { get; }
    internal int NativeCodeOffset { get; }
    internal int ResidentSegmentSize { get; }
}
