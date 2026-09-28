/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Text;
using CopperMod.Amiga.Core;

namespace CopperMod.Amiga.Firmware;

/// <summary>
/// Guest-visible graphics.library images. Native fields share SDK offsets;
/// compact-host strings retain their historical, explicitly non-native layout.
/// </summary>
internal static class GraphicsLibraryImageLayout
{
    internal const int LibraryPrefixSize = 0x22;
    internal const int GfxBaseTextFonts = 0x8C;
    internal const int GfxBaseTextFontsHead = 0x8C;
    internal const int GfxBaseTextFontsTail = 0x90;
    internal const int GfxBaseTextFontsTailPred = 0x94;
    internal const int GfxBaseTextFontsType = 0x98;
    internal const int GfxBaseTextFontsPad = 0x99;
    internal const int GfxBaseDefaultFont = 0x9A;
    internal const int GfxBaseActiView = 0x22;
    // gfxbase.i: TOF_WaitQ at0xC0 occupies LH_SIZE=14 bytes.
    internal const int GfxBaseDisplayFlags = 0xCE;
    internal const int GfxBaseChipRevBits0 = 0xEC;
    // The public GfxBase tail is laid out as the native 68k gfxbase.h
    // structure.  The compact compatibility image does not need this tail,
    // but a future CopperSharp68k image may elect to include it verbatim.
    // Keep the offsets here (rather than deriving them in a host wrapper) so
    // optional native fields can be initialized without changing the public
    // prefix or the negative-vector table.
    internal const int GfxBaseCurrentMonitor = 0x17C;
    internal const int GfxBaseMonitorList = 0x180;
    internal const int GfxBaseMonitorListHead = GfxBaseMonitorList;
    internal const int GfxBaseMonitorListTail = GfxBaseMonitorList + 4;
    internal const int GfxBaseMonitorListTailPred = GfxBaseMonitorList + 8;
    internal const int GfxBaseMonitorListType = GfxBaseMonitorList + 12;
    internal const int GfxBaseMonitorListPad = GfxBaseMonitorList + 13;
    internal const int GfxBaseDefaultMonitor = 0x18E;
    internal const int GfxBaseMonitorListSemaphore = 0x192;
    internal const int GfxBaseDisplayInfoDataBase = 0x196;
    internal const int GfxBaseTopLine = 0x19A;
    internal const int GfxBaseActiViewCprSemaphore = 0x19C;
    internal const int GfxBaseNativeSize = 0x220;
    internal const int MinimumImageSize = 0xF0;
    internal const int DefaultNameOffset = 0xA0;
    internal const int DefaultIdStringOffset = 0xB4;
    internal const int NativeNameOffset = GfxBaseNativeSize;
    internal const int NativeIdStringOffset = NativeNameOffset + 0x14;
    internal const int NativeMinimumImageSize = 0x24C;
    // Opt-in private runtime descriptor, outside the public GfxBase and
    // native strings. All offsets/widths are guest ABI values, not host sizes.
    internal const int NativeRuntimeDescriptorTag = 0x250;
    internal const int NativeRuntimeDescriptorVersion = 0x254;
    internal const int NativeRuntimeDescriptorOwner = 0x258;
    internal const int NativeRuntimeDescriptorDatabase = 0x25C;
    internal const int NativeRuntimeDescriptorDatabaseSize = 0x260;
    internal const int NativeRuntimeImageSize = 0x264;
    internal const uint NativeRuntimeDescriptorValidTag = 0x434D444Fu; // CMDO
    internal const uint NativeRuntimeDescriptorCurrentVersion = 1;
    // Independent opt-in ownership for the default MonitorSpec/name allocation.
    // Preserve the existing CMDO/CMDB contract and its 0x264-byte image extent.
    internal const int NativeMonitorDescriptorTag = 0x264;
    internal const int NativeMonitorDescriptorVersion = 0x268;
    internal const int NativeMonitorDescriptorOwner = 0x26C;
    internal const int NativeMonitorDescriptorAllocation = 0x270;
    internal const int NativeMonitorDescriptorAllocationSize = 0x274;
    // Version2 records the registered family independently of mutable public
    // names/timing flags. This is a guest ULONG, not a host pointer or enum.
    internal const int NativeMonitorDescriptorId = 0x278;
    internal const int NativeMonitorImageSize = 0x27C;
    internal const uint NativeMonitorDescriptorValidTag = 0x434D4D4Fu; // CMMO
    internal const uint NativeMonitorDescriptorCurrentVersion = 2;

    /// <summary>
    /// Creates the positive guest image.  <paramref name="reportedPositiveSize"/>
    /// is the value written to <c>lib_PosSize</c>; it is intentionally
    /// separate from the backing image length because the host shim keeps a
    /// compatibility envelope beyond its historical public size.
    /// </summary>
    internal static byte[] CreateGuestImage(
        uint libraryBase,
        int imageLength,
        int reportedNegativeSize,
        int reportedPositiveSize,
        ushort version,
        ushort revision,
        string name,
        string idString,
        GraphicsLibraryImageProfile profile = GraphicsLibraryImageProfile.CompactHost,
        bool includeNativeRuntimeDescriptor = false,
        bool includeNativeMonitorDescriptor = false)
    {
        if ((libraryBase & 1) != 0) throw new ArgumentException("A library base must be word aligned.", nameof(libraryBase));
        if (imageLength < MinimumImageSize) throw new ArgumentOutOfRangeException(nameof(imageLength));
        if (reportedNegativeSize < 0 || reportedNegativeSize > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(reportedNegativeSize));
        if (reportedPositiveSize < 0 || reportedPositiveSize > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(reportedPositiveSize));
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(idString)) throw new ArgumentException("Library names and ID strings are required.");
        if (profile is not (GraphicsLibraryImageProfile.CompactHost or GraphicsLibraryImageProfile.NativePal or GraphicsLibraryImageProfile.NativeNtsc))
            throw new ArgumentOutOfRangeException(nameof(profile));

        var nativeLayout = profile != GraphicsLibraryImageProfile.CompactHost;
        if (includeNativeMonitorDescriptor && (!nativeLayout ||
            imageLength < NativeMonitorImageSize || reportedPositiveSize < NativeMonitorImageSize))
            throw new ArgumentException("The monitor descriptor requires an explicitly sized native image.", nameof(includeNativeMonitorDescriptor));
        if (includeNativeRuntimeDescriptor && (!nativeLayout ||
            imageLength < NativeRuntimeImageSize || reportedPositiveSize < NativeRuntimeImageSize))
            throw new ArgumentException("The runtime descriptor requires an explicitly sized native image.", nameof(includeNativeRuntimeDescriptor));
        if (nativeLayout && (imageLength < NativeMinimumImageSize ||
            reportedPositiveSize < NativeMinimumImageSize || reportedPositiveSize > imageLength))
            throw new ArgumentException("A native allocation must include its fields and library strings.", nameof(reportedPositiveSize));
        if (nativeLayout && libraryBase > uint.MaxValue - (uint)(imageLength - 1))
            throw new ArgumentOutOfRangeException(nameof(libraryBase), "The native image must not wrap guest address space.");

        var nameBytes = Encoding.ASCII.GetBytes(name);
        var idBytes = Encoding.ASCII.GetBytes(idString);
        var nameOffset = nativeLayout ? NativeNameOffset : DefaultNameOffset;
        var idOffset = nativeLayout ? NativeIdStringOffset : DefaultIdStringOffset;
        var availableLength = nativeLayout ? reportedPositiveSize : imageLength;
        if (nameOffset + (long)nameBytes.Length + 1 > availableLength ||
            idOffset + (long)idBytes.Length + 1 > availableLength ||
            (nativeLayout && nameBytes.Length + 1L > idOffset - nameOffset) ||
            ((includeNativeRuntimeDescriptor || includeNativeMonitorDescriptor) &&
                idOffset + (long)idBytes.Length + 1 > NativeRuntimeDescriptorTag))
        {
            throw new ArgumentException("The image is too small for its library strings.", nameof(imageLength));
        }

        var image = new byte[imageLength];
        image[0x08] = 9; // NT_LIBRARY
        BigEndian.WriteUInt32(image, 0x0A, checked(libraryBase + (uint)nameOffset));
        BigEndian.WriteUInt16(image, 0x10, (ushort)reportedNegativeSize);
        BigEndian.WriteUInt16(image, 0x12, (ushort)reportedPositiveSize);
        BigEndian.WriteUInt16(image, 0x14, version);
        BigEndian.WriteUInt16(image, 0x16, revision);
        BigEndian.WriteUInt32(image, 0x18, checked(libraryBase + (uint)idOffset));
        if (nativeLayout)
            BigEndian.WriteUInt16(image, GfxBaseDisplayFlags,
                profile == GraphicsLibraryImageProfile.NativeNtsc ? (ushort)1 : (ushort)4);

        // Empty Exec MinList sentinels.  Fonts are linked later by the
        // graphics font-list backend or by native graphics.library code.
        BigEndian.WriteUInt32(image, GfxBaseTextFontsHead, checked(libraryBase + (uint)GfxBaseTextFontsTail));
        BigEndian.WriteUInt32(image, GfxBaseTextFontsTail, 0);
        BigEndian.WriteUInt32(image, GfxBaseTextFontsTailPred, checked(libraryBase + (uint)GfxBaseTextFonts));
        image[GfxBaseTextFontsType] = 0;
        image[GfxBaseTextFontsPad] = 0;

        // When a caller deliberately asks for the full native GfxBase
        // envelope, initialize the embedded MonitorList as an empty Exec
        // MinList.  The normal CopperScreen image remains compact (and keeps
        // the historical 0xF0 positive size), while a native 68k image can
        // opt into a self-consistent monitor-list layout without a second
        // host-side structure definition.
        if (imageLength >= GfxBaseNativeSize)
        {
            var monitorList = checked(libraryBase + (uint)GfxBaseMonitorList);
            BigEndian.WriteUInt32(image, GfxBaseMonitorListHead, checked(monitorList + 4u));
            BigEndian.WriteUInt32(image, GfxBaseMonitorListTail, 0);
            BigEndian.WriteUInt32(image, GfxBaseMonitorListTailPred, monitorList);
            image[GfxBaseMonitorListType] = 0;
            image[GfxBaseMonitorListPad] = 0;
        }

        WriteAscii(image, nameOffset, nameBytes);
        WriteAscii(image, idOffset, idBytes);
        // No live ownership is asserted by an image template. Owner/database
        // stay zero (no relocation needed); a publisher installs them and
        // commits ValidTag last only after the owned CMDB is initialized.
        if (includeNativeRuntimeDescriptor)
            BigEndian.WriteUInt32(image, NativeRuntimeDescriptorVersion, NativeRuntimeDescriptorCurrentVersion);
        if (includeNativeMonitorDescriptor)
            BigEndian.WriteUInt32(image, NativeMonitorDescriptorVersion, NativeMonitorDescriptorCurrentVersion);
        return image;
    }

    private static void WriteAscii(Span<byte> destination, int offset, ReadOnlySpan<byte> value)
    {
        value.CopyTo(destination[offset..]);
        destination[offset + value.Length] = 0;
    }
}

internal enum GraphicsLibraryImageProfile
{
    CompactHost,
    NativePal,
    NativeNtsc
}
