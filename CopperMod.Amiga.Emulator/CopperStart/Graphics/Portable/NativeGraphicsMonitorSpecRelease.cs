using System;
using System.Collections.Generic;
using CopperMod.Amiga.Firmware;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>Native teardown of a quiescent, exactly owned default monitor.</summary>
internal static class NativeGraphicsMonitorSpecRelease
{
    // D0=GfxBase,A6=ExecBase. Return base on release or zero on decline;
    // preserve D2-D7/A2-A6. Caller serializes lifetime mutations and ensures
    // external borrowed pointers are quiescent. This is not CloseMonitor or
    // a complete driver/graphics Expunge. CMDO ownership is never modified.
    internal static byte[] Build(bool ntsc) => Build(ntsc, false, false);

    // Use for registered publication; release this node before its CMDB owner.
    internal static byte[] BuildRegistered(bool ntsc, bool includeMonitorMutation = false)
        => Build(ntsc, true, includeMonitorMutation);

    private static byte[] Build(bool ntsc, bool registered, bool includeMonitorMutation)
    {
        var image = GraphicsMonitorSpecImage.CreateNativeDefaultImage(ntsc);
        var code = new List<byte>();
        var failures = new List<int>();
        void Word(ushort value) { code.Add((byte)(value >> 8)); code.Add((byte)value); }
        void Long(uint value) { Word((ushort)(value >> 16)); Word((ushort)value); }
        void Fail(ushort opcode) { Word(opcode); failures.Add(code.Count); Word(0); }
        void CompareLong(ushort opcode, int offset, uint value)
        { Word(opcode); Long(value); Word((ushort)offset); Fail(0x6600); }
        void CompareSmall(ushort opcode, int offset, ushort value)
        { Word(opcode); Word(value); Word((ushort)offset); Fail(0x6600); }
        Word(0x48E7); Word(0x3020); // MOVEM.L D2-D3/A2,-(SP)
        Word(0x4A80); Fail(0x6700);
        Word(0x0800); Word(0); Fail(0x6600);
        Word(0x0C80); Long(uint.MaxValue - (GraphicsLibraryImageLayout.NativeMonitorImageSize - 1u)); Fail(0x6200);
        Word(0x2440); // MOVEA.L D0,A2
        Word(0x0C6A); Word(GraphicsLibraryImageLayout.NativeMonitorImageSize); Word(0x12); Fail(0x6500);
        CompareLong(0x0CAA, GraphicsLibraryImageLayout.NativeMonitorDescriptorTag, GraphicsLibraryImageLayout.NativeMonitorDescriptorValidTag);
        CompareLong(0x0CAA, GraphicsLibraryImageLayout.NativeMonitorDescriptorVersion, GraphicsLibraryImageLayout.NativeMonitorDescriptorCurrentVersion);
        CompareLong(0x0CAA, GraphicsLibraryImageLayout.NativeMonitorDescriptorId, ntsc ? GraphicsModeIds.NtscMonitor : GraphicsModeIds.PalMonitor);
        Word(0xB0AA); Word(GraphicsLibraryImageLayout.NativeMonitorDescriptorOwner); Fail(0x6600);
        CompareLong(0x0CAA, GraphicsLibraryImageLayout.NativeMonitorDescriptorAllocationSize, (uint)image.Length);
        Word(0x262A); Word(GraphicsLibraryImageLayout.NativeMonitorDescriptorAllocation); // D3=owned block
        Word(0x4A83); Fail(0x6700);
        Word(0x0803); Word(0); Fail(0x6600);
        Word(0x0C83); Long(uint.MaxValue - ((uint)image.Length - 1)); Fail(0x6200);
        // Prove public identity without following any foreign list pointer.
        foreach (var offset in new[] { GraphicsLibraryImageLayout.GfxBaseDefaultMonitor,
            GraphicsLibraryImageLayout.GfxBaseMonitorListHead, GraphicsLibraryImageLayout.GfxBaseMonitorListTailPred })
        { Word(0xB6AA); Word((ushort)offset); Fail(0x6600); }
        CompareLong(0x0CAA, GraphicsLibraryImageLayout.GfxBaseMonitorListTail, 0);
        CompareSmall(0x0C6A, GraphicsLibraryImageLayout.GfxBaseMonitorListType, 0);
        CompareLong(0x0CAA, GraphicsLibraryImageLayout.GfxBaseActiView, 0);
        CompareLong(0x0CAA, GraphicsLibraryImageLayout.GfxBaseCurrentMonitor, 0);
        Word(0x2043); // MOVEA.L D3,A0: only now dereference our validated allocation
        Word(0x200A); Word(0xB0A8); Word((ushort)GraphicsLayouts.ExtendedNodeLibrary); Fail(0x6600);
        foreach (var (nodeOffset, baseOffset) in new[] { (0, GraphicsLibraryImageLayout.GfxBaseMonitorListTail),
            (4, GraphicsLibraryImageLayout.GfxBaseMonitorList) })
        {
            Word(0x43EA); Word((ushort)baseOffset); // LEA d16(A2),A1
            Word(0x2009); Word(0xB0A8); Word((ushort)nodeOffset); Fail(0x6600);
        }
        CompareSmall(0x0C28, GraphicsLayouts.MonitorSpecNodeType, 18);
        CompareSmall(0x0C68, GraphicsLayouts.MonitorSpecNodeSubsystem, 0x0204);
        CompareSmall(0x0C68, GraphicsLayouts.MonitorSpecFlags, ntsc ? (ushort)1 : (ushort)2);
        CompareSmall(0x0C68, GraphicsLayouts.MonitorSpecOpenCount, 0);
        // Only an empty resident is handled here. Driver callback/resources
        // and populated display-info lists need their own teardown protocol.
        foreach (var offset in new[] { GraphicsLayouts.ExtendedNodeInit, GraphicsLayouts.MonitorSpecSpecial, GraphicsLayouts.MonitorSpecTransform,
            GraphicsLayouts.MonitorSpecTranslate, GraphicsLayouts.MonitorSpecScale, GraphicsLayouts.MonitorSpecMaxOScan,
            GraphicsLayouts.MonitorSpecVideoScan, GraphicsLayouts.MonitorSpecDisplayCompatible,
            GraphicsLayouts.MonitorSpecMergeCopper, GraphicsLayouts.MonitorSpecLoadView, GraphicsLayouts.MonitorSpecKillView,
            GraphicsLayouts.MonitorSpecDisplayInfoDataBaseTail, GraphicsLayouts.MonitorSpecDisplayInfoSemaphoreOwner,
            GraphicsLayouts.MonitorSpecDisplayInfoSemaphoreWaitQueue + 4,
            GraphicsLayouts.MonitorSpecDisplayInfoSemaphore + 0x1C,
            GraphicsLayouts.MonitorSpecDisplayInfoSemaphore + 0x20,
            GraphicsLayouts.MonitorSpecDisplayInfoSemaphore + 0x24 })
            CompareLong(0x0CA8, offset, 0);
        CompareSmall(0x0C68, GraphicsLayouts.MonitorSpecDisplayInfoDataBaseType, 0);
        CompareSmall(0x0C28, GraphicsLayouts.MonitorSpecDisplayInfoSemaphoreNodeType, 15);
        CompareSmall(0x0C68, GraphicsLayouts.MonitorSpecDisplayInfoSemaphoreNestCount, 0);
        CompareSmall(0x0C68, GraphicsLayouts.MonitorSpecDisplayInfoSemaphoreQueueCount, ushort.MaxValue);
        foreach (var offset in GraphicsMonitorSpecImage.NativeSelfPointerOffsets)
        {
            var relative = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(image.AsSpan(offset, 4));
            Word(0x2003); Word(0x0680); Long(relative); // MOVE.L D3,D0; ADDI.L #relative,D0
            Word(0xB0A8); Word((ushort)offset); Fail(0x6600);
        }
        for (var offset = GraphicsLayouts.MonitorSpecSize; offset < image.Length; offset++)
            CompareSmall(0x0C28, offset, image[offset]); // canonical inline name, terminator and padding

        // All admission precedes the first ownership write. Invalidate FIRST;
        // make the library fresh/inert before FreeMem may yield to a new owner.
        if (registered)
            NativeGraphicsMonitorRegistrationAdmission.Append(code, failures, ntsc,
                includeMonitorMutation);
        Word(0x42AA); Word(GraphicsLibraryImageLayout.NativeMonitorDescriptorTag);
        if (registered)
            foreach (var offset in new[] { NativeGraphicsMonitorRegistrationAdmission.Slot(true),
                NativeGraphicsMonitorRegistrationAdmission.Slot(false) })
            {
                Word(0xB6A9); Word((ushort)offset); // CMP.L slot(A1),D3
                Word(0x6604); // BNE.S past CLR: retain unrelated borrowed registrations
                Word(0x42A9); Word((ushort)offset);
            }
        Word(0x43EA); Word(GraphicsLibraryImageLayout.GfxBaseMonitorListTail);
        Word(0x2549); Word(GraphicsLibraryImageLayout.GfxBaseMonitorListHead); // MOVE.L A1,d16(A2)
        Word(0x43EA); Word(GraphicsLibraryImageLayout.GfxBaseMonitorList);
        Word(0x2549); Word(GraphicsLibraryImageLayout.GfxBaseMonitorListTailPred);
        foreach (var offset in new[] { GraphicsLibraryImageLayout.GfxBaseDefaultMonitor,
            GraphicsLibraryImageLayout.NativeMonitorDescriptorOwner, GraphicsLibraryImageLayout.NativeMonitorDescriptorAllocation,
            GraphicsLibraryImageLayout.NativeMonitorDescriptorAllocationSize, GraphicsLibraryImageLayout.NativeMonitorDescriptorId })
        { Word(0x42AA); Word((ushort)offset); }
        Word(0x2243); Word(0x203C); Long((uint)image.Length);
        Word(0x4EAE); Word(unchecked((ushort)-210)); // FreeMem(owned block,exact extent)
        Word(0x200A); Word(0x6000); var success = code.Count; Word(0);
        foreach (var failure in failures) Patch(failure, code.Count);
        Word(0x7000);
        Patch(success, code.Count);
        Word(0x4CDF); Word(0x040C); Word(0x4E75);
        return code.ToArray();

        void Patch(int extension, int target)
        {
            var displacement = checked((short)(target - extension));
            code[extension] = (byte)((ushort)displacement >> 8);
            code[extension + 1] = (byte)displacement;
        }
    }
}
