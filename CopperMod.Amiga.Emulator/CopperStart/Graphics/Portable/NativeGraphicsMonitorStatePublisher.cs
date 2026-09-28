using System.Collections.Generic;
using CopperMod.Amiga.Firmware;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>Native boot-time publication into a fresh, inert runtime descriptor.</summary>
internal static class NativeGraphicsMonitorStatePublisher
{
    // D0=initialized GfxBase, A6=ExecBase. Return base on success, zero on
    // decline/failure; preserve D2-D7/A2-A6. This is a fresh-owner constructor,
    // not adoption, replacement or teardown of an existing provider's state.
    // The lifecycle owner serializes calls; this routine does not supply an
    // inter-task lock. AllocMem must return a distinct writable allocation.
    internal static byte[] Build(bool supportsEcsDisplay, bool defaultMonitorNtsc = false,
        bool includeMonitorMutation = false)
    {
        var image = GraphicsDisplayDatabase.CreateNativeDatabaseImage(
            supportsEcsDisplay, defaultMonitorNtsc, includeMonitorMutation);
        var code = new List<byte>();
        var failures = new List<int>();
        var rollbacks = new List<int>();
        void Word(ushort value) { code.Add((byte)(value >> 8)); code.Add((byte)value); }
        void Long(uint value) { Word((ushort)(value >> 16)); Word((ushort)value); }
        void Branch(ushort opcode, List<int> targets) { Word(opcode); targets.Add(code.Count); Word(0); }
        void Patch(int extension, int target)
        {
            var displacement = checked((short)(target - extension));
            code[extension] = (byte)((ushort)displacement >> 8);
            code[extension + 1] = (byte)displacement;
        }
        void InertDescriptor(List<int> targets)
        {
            foreach (var (offset, value) in new (int, uint)[]
            {
                (GraphicsLibraryImageLayout.NativeRuntimeDescriptorTag, 0),
                (GraphicsLibraryImageLayout.NativeRuntimeDescriptorVersion, 1),
                (GraphicsLibraryImageLayout.NativeRuntimeDescriptorOwner, 0),
                (GraphicsLibraryImageLayout.NativeRuntimeDescriptorDatabase, 0),
                (GraphicsLibraryImageLayout.NativeRuntimeDescriptorDatabaseSize, 0),
                (GraphicsLayouts.GfxBaseDisplayInfoDataBase, 0)
            })
            {
                Word(0x0CAA); Long(value); Word((ushort)offset); // CMPI.L #value,d16(A2)
                Branch(0x6600, targets);
            }
        }
        Word(0x48E7); Word(0x3020); // MOVEM.L D2-D3/A2,-(SP)
        Word(0x4A80); Branch(0x6700, failures); // TST.L D0; BEQ
        Word(0x0800); Word(0); Branch(0x6600, failures); // BTST #0,D0
        Word(0x0C80); Long(uint.MaxValue - (GraphicsLibraryImageLayout.NativeRuntimeImageSize - 1u));
        Branch(0x6200, failures);
        Word(0x2440); // MOVEA.L D0,A2
        Word(0x0C6A); Word(GraphicsLibraryImageLayout.NativeRuntimeImageSize); Word(0x12);
        Branch(0x6500, failures); // CMPI.W #required,lib_PosSize(A2); BCS
        InertDescriptor(failures);
        Word(0x203C); Long((uint)image.Length);
        Word(0x223C); Long(0x00010001); // MEMF_PUBLIC|MEMF_CLEAR
        Word(0x4EAE); Word(unchecked((ushort)-198)); // JSR AllocMem(A6)
        Word(0x4A80); Branch(0x6700, failures);
        Word(0x2600); // MOVE.L D0,D3: owned candidate
        Word(0x0800); Word(0); Branch(0x6600, rollbacks);
        Word(0x0800); Word(1); Branch(0x6600, rollbacks);
        Word(0x0C80); Long(uint.MaxValue - ((uint)image.Length - 1));
        Branch(0x6200, rollbacks);
        // Allocation can yield to another owner. Recheck without reading any
        // foreign pointer; free only our returned candidate on a changed claim.
        InertDescriptor(rollbacks);
        Word(0x2043); // MOVEA.L D3,A0
        Word(0x43FA); var imageExtension = code.Count; Word(0); // LEA image(PC),A1
        Word(0x343C); Word(checked((ushort)(image.Length - 1)));
        var copy = code.Count;
        Word(0x10D9); // MOVE.B (A1)+,(A0)+
        Word(0x51CA); var loop = code.Count; Word(0); Patch(loop, copy);
        Word(0x200A); // MOVE.L A2,D0
        Word(0x2540); Word(GraphicsLibraryImageLayout.NativeRuntimeDescriptorOwner);
        Word(0x2543); Word(GraphicsLibraryImageLayout.NativeRuntimeDescriptorDatabase);
        Word(0x203C); Long((uint)image.Length);
        Word(0x2540); Word(GraphicsLibraryImageLayout.NativeRuntimeDescriptorDatabaseSize);
        Word(0x2543); Word((ushort)GraphicsLayouts.GfxBaseDisplayInfoDataBase);
        Word(0x203C); Long(GraphicsLibraryImageLayout.NativeRuntimeDescriptorValidTag);
        Word(0x2540); Word(GraphicsLibraryImageLayout.NativeRuntimeDescriptorTag); // validity LAST
        Word(0x200A); // return library base
        Word(0x6000); var success = code.Count; Word(0);

        foreach (var rollback in rollbacks) Patch(rollback, code.Count);
        Word(0x2243); // MOVEA.L D3,A1
        Word(0x203C); Long((uint)image.Length);
        Word(0x4EAE); Word(unchecked((ushort)-210)); // JSR FreeMem(A6)
        foreach (var failure in failures) Patch(failure, code.Count);
        Word(0x7000); // MOVEQ #0,D0
        Patch(success, code.Count);
        Word(0x4CDF); Word(0x040C); // MOVEM.L (SP)+,D2-D3/A2
        Word(0x4E75);
        Patch(imageExtension, code.Count);
        code.AddRange(image);
        if ((code.Count & 1) != 0) code.Add(0);
        return code.ToArray();
    }
}
