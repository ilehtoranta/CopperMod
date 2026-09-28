using System.Collections.Generic;
using CopperMod.Amiga.Firmware;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>Position-independent default MonitorSpec and name initialization.</summary>
internal static class NativeGraphicsMonitorSpecInitializer
{
    // D0=private writable allocation, D1=allocation bytes, A0=GfxBase backlink.
    // Return the monitor base or zero before writes on invalid admission.
    // Preserve D2-D7/A2-A6. No allocation, list publication or ownership claim.
    internal static byte[] Build(bool ntsc)
    {
        var image = GraphicsMonitorSpecImage.CreateNativeDefaultImage(ntsc);
        var code = new List<byte>();
        var failures = new List<int>();
        void Word(ushort value) { code.Add((byte)(value >> 8)); code.Add((byte)value); }
        void Long(uint value) { Word((ushort)(value >> 16)); Word((ushort)value); }
        void Fail(ushort opcode) { Word(opcode); failures.Add(code.Count); Word(0); }
        void Patch(int extension, int target)
        {
            var displacement = checked((short)(target - extension));
            code[extension] = (byte)((ushort)displacement >> 8);
            code[extension + 1] = (byte)displacement;
        }
        Word(0x4A80); Fail(0x6700); // TST.L D0
        Word(0x0800); Word(0); Fail(0x6600);
        Word(0x0C80); Long(uint.MaxValue - ((uint)image.Length - 1)); Fail(0x6200);
        Word(0x0C81); Long((uint)image.Length); Fail(0x6500); // supplied extent
        Word(0x2208); // MOVE.L A0,D1: backlink admission does not dereference GfxBase
        Word(0x4A81); Fail(0x6700);
        Word(0x0801); Word(0); Fail(0x6600);
        Word(0x0C81); Long(uint.MaxValue - (GraphicsLibraryImageLayout.LibraryPrefixSize - 1u));
        Fail(0x6200);
        Word(0x2F0A); // MOVE.L A2,-(SP)
        Word(0x2448); // MOVEA.L A0,A2
        Word(0x2040); // MOVEA.L D0,A0
        Word(0x43FA); var source = code.Count; Word(0);
        Word(0x323C); Word(checked((ushort)(image.Length - 1)));
        var copy = code.Count;
        Word(0x10D9); Word(0x51C9); var loop = code.Count; Word(0); Patch(loop, copy);
        Word(0x2040); // MOVEA.L D0,A0
        foreach (var offset in GraphicsMonitorSpecImage.NativeSelfPointerOffsets)
        {
            Word(0xD1A8); Word((ushort)offset); // ADD.L D0,relative-pointer(A0)
        }
        Word(0x214A); Word((ushort)GraphicsLayouts.ExtendedNodeLibrary); // MOVE.L A2,backlink(A0)
        Word(0x245F); Word(0x4E75); // MOVEA.L (SP)+,A2; RTS
        foreach (var failure in failures) Patch(failure, code.Count);
        Word(0x7000); Word(0x4E75);
        Patch(source, code.Count);
        code.AddRange(image);
        return code.ToArray();
    }
}
