using System.Collections.Generic;
using CopperMod.Amiga.Firmware;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>Shared admission for explicitly paired CMDB3/MonitorSpec lifecycle.</summary>
internal static class NativeGraphicsMonitorRegistrationAdmission
{
    // A2 is an already admitted full native GfxBase. Return A1=owned CMDB3;
    // scratch D0 only, preserving the caller's candidate in D3. No writes.
    internal static void Append(List<byte> code, List<int> failures, bool ntsc,
        bool includeMonitorMutation = false)
    {
        var databaseSize = includeMonitorMutation
            ? GraphicsDisplayDatabase.NativeDatabaseMutationSize
            : GraphicsDisplayDatabase.NativeDatabaseSize;
        void Word(ushort value) { code.Add((byte)(value >> 8)); code.Add((byte)value); }
        void Long(uint value) { Word((ushort)(value >> 16)); Word((ushort)value); }
        void Fail(ushort opcode) { Word(opcode); failures.Add(code.Count); Word(0); }
        void Compare(ushort opcode, int offset, uint value)
        { Word(opcode); Long(value); Word((ushort)offset); Fail(0x6600); }
        Compare(0x0CAA, GraphicsLibraryImageLayout.NativeRuntimeDescriptorTag,
            GraphicsLibraryImageLayout.NativeRuntimeDescriptorValidTag);
        Compare(0x0CAA, GraphicsLibraryImageLayout.NativeRuntimeDescriptorVersion, 1);
        Word(0x200A); Word(0xB0AA); Word(GraphicsLibraryImageLayout.NativeRuntimeDescriptorOwner);
        Fail(0x6600);
        Compare(0x0CAA, GraphicsLibraryImageLayout.NativeRuntimeDescriptorDatabaseSize,
            (uint)databaseSize);
        Word(0x202A); Word(GraphicsLibraryImageLayout.NativeRuntimeDescriptorDatabase);
        Word(0xB0AA); Word((ushort)GraphicsLayouts.GfxBaseDisplayInfoDataBase); Fail(0x6600);
        Word(0x4A80); Fail(0x6700);
        Word(0x0800); Word(0); Fail(0x6600);
        Word(0x0800); Word(1); Fail(0x6600);
        Word(0x0C80); Long(uint.MaxValue - ((uint)databaseSize - 1));
        Fail(0x6200);
        Word(0x2240); // MOVEA.L D0,A1; only dereference after envelope checks
        Compare(0x0CA9, 0, GraphicsDisplayDatabase.NativeDatabaseMagic);
        Compare(0x0CA9, 4, GraphicsDisplayDatabase.NativeDatabaseVersion);
        Compare(0x0CA9, 8, (uint)databaseSize);
        Compare(0x0CA9, GraphicsDisplayDatabase.NativeMonitorPositionsOffset, GraphicsModeIds.NtscMonitor);
        Compare(0x0CA9, GraphicsDisplayDatabase.NativeMonitorPositionsOffset +
            GraphicsDisplayDatabase.NativeMonitorPositionRecordSize, GraphicsModeIds.PalMonitor);
        Compare(0x0CA9, GraphicsDisplayDatabase.NativeDefaultMonitorIdOffset,
            ntsc ? GraphicsModeIds.NtscMonitor : GraphicsModeIds.PalMonitor);
    }

    internal static int Slot(bool ntsc) => GraphicsDisplayDatabase.NativeMonitorRegistrationsOffset + (ntsc ? 0 : 4);
}
