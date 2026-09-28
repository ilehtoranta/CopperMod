using System.Collections.Generic;
using CopperMod.Amiga.Firmware;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>Native release of one explicitly owned CMDB allocation.</summary>
internal static class NativeGraphicsMonitorStateRelease
{
    // D0=GfxBase, A6=valid ExecBase. Return base when released or zero on
    // decline. Preserve D2-D7/A2-A6. This destroys current/original state;
    // it is not the host core's capture-and-rebind operation or a full Expunge.
    // The lifecycle owner serializes calls; no inter-task lock is implied.
    internal static byte[] Build(bool includeMonitorMutation = false)
    {
        var databaseSize = includeMonitorMutation
            ? GraphicsDisplayDatabase.NativeDatabaseMutationSize
            : GraphicsDisplayDatabase.NativeDatabaseSize;
        var code = new List<byte>();
        var failures = new List<int>();
        void Word(ushort value) { code.Add((byte)(value >> 8)); code.Add((byte)value); }
        void Long(uint value) { Word((ushort)(value >> 16)); Word((ushort)value); }
        void Fail(ushort opcode) { Word(opcode); failures.Add(code.Count); Word(0); }
        void Compare(ushort opcode, int offset, uint value)
        {
            Word(opcode); Long(value); Word((ushort)offset); Fail(0x6600);
        }
        Word(0x48E7); Word(0x3020); // MOVEM.L D2-D3/A2,-(SP)
        Word(0x4A80); Fail(0x6700);
        Word(0x0800); Word(0); Fail(0x6600);
        Word(0x0C80); Long(uint.MaxValue - (GraphicsLibraryImageLayout.NativeRuntimeImageSize - 1u));
        Fail(0x6200);
        Word(0x2440); // MOVEA.L D0,A2
        Word(0x0C6A); Word(GraphicsLibraryImageLayout.NativeRuntimeImageSize); Word(0x12);
        Fail(0x6500);
        Compare(0x0CAA, GraphicsLibraryImageLayout.NativeRuntimeDescriptorTag,
            GraphicsLibraryImageLayout.NativeRuntimeDescriptorValidTag);
        Compare(0x0CAA, GraphicsLibraryImageLayout.NativeRuntimeDescriptorVersion, 1);
        Word(0xB0AA); Word(GraphicsLibraryImageLayout.NativeRuntimeDescriptorOwner); // CMP.L owner(A2),D0
        Fail(0x6600);
        Compare(0x0CAA, GraphicsLibraryImageLayout.NativeRuntimeDescriptorDatabaseSize,
            (uint)databaseSize);
        Word(0x262A); Word(GraphicsLibraryImageLayout.NativeRuntimeDescriptorDatabase); // MOVE.L db(A2),D3
        Word(0xB6AA); Word((ushort)GraphicsLayouts.GfxBaseDisplayInfoDataBase);
        Fail(0x6600); // public pointer must match before any database dereference
        Word(0x4A83); Fail(0x6700);
        Word(0x0803); Word(0); Fail(0x6600);
        Word(0x0803); Word(1); Fail(0x6600);
        Word(0x0C83); Long(uint.MaxValue - ((uint)databaseSize - 1));
        Fail(0x6200);
        Word(0x2043); // MOVEA.L D3,A0
        Compare(0x0CA8, 0, GraphicsDisplayDatabase.NativeDatabaseMagic);
        Compare(0x0CA8, 4, GraphicsDisplayDatabase.NativeDatabaseVersion);
        Compare(0x0CA8, 8, (uint)databaseSize);
        Compare(0x0CA8, GraphicsDisplayDatabase.NativeMonitorPositionsOffset, 0x11000);
        Compare(0x0CA8, GraphicsDisplayDatabase.NativeMonitorPositionsOffset +
            GraphicsDisplayDatabase.NativeMonitorPositionRecordSize, 0x21000);
        // Invalidate FIRST. Restore the inert claim before FreeMem may yield
        // to another owner, and perform no descriptor writes after that call.
        foreach (var offset in new[] { GraphicsLibraryImageLayout.NativeRuntimeDescriptorTag,
            GraphicsLayouts.GfxBaseDisplayInfoDataBase, GraphicsLibraryImageLayout.NativeRuntimeDescriptorOwner,
            GraphicsLibraryImageLayout.NativeRuntimeDescriptorDatabase,
            GraphicsLibraryImageLayout.NativeRuntimeDescriptorDatabaseSize })
        {
            Word(0x42AA); Word((ushort)offset); // CLR.L d16(A2)
        }
        Word(0x2243); // MOVEA.L D3,A1
        Word(0x203C); Long((uint)databaseSize);
        Word(0x4EAE); Word(unchecked((ushort)-210)); // JSR FreeMem(A6)
        Word(0x200A); // MOVE.L A2,D0
        Word(0x6000); var success = code.Count; Word(0);
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
