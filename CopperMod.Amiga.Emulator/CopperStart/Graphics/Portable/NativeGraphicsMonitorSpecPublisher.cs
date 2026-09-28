using System.Collections.Generic;
using CopperMod.Amiga.Firmware;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>Native fresh-owner publication of a default MonitorSpec and name.</summary>
internal static class NativeGraphicsMonitorSpecPublisher
{
    // D0=initialized GfxBase, A6=valid ExecBase; return base or zero.
    // Preserve D2-D7/A2-A6. Caller serializes lifetime mutations; AllocMem
    // must return a distinct writable allocation. No foreign ownership adoption.
    internal static byte[] Build(bool ntsc) => Build(ntsc, false);

    // Pair with BuildRegistered release while the owned CMDB3 is still alive.
    internal static byte[] BuildRegistered(bool ntsc, bool includeMonitorMutation = false)
        => Build(ntsc, true, includeMonitorMutation);

    private static byte[] Build(bool ntsc, bool register, bool includeMonitorMutation = false)
    {
        var size = GraphicsMonitorSpecImage.CreateNativeDefaultImage(ntsc).Length;
        var initializer = NativeGraphicsMonitorSpecInitializer.Build(ntsc);
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
        void InertClaim(List<int> targets)
        {
            Word(0x0C6A); Word(GraphicsLibraryImageLayout.NativeMonitorImageSize); Word(0x12);
            Branch(0x6500, targets);
            foreach (var (offset, value) in new (int, uint)[]
            {
                (GraphicsLibraryImageLayout.NativeMonitorDescriptorTag, 0),
                (GraphicsLibraryImageLayout.NativeMonitorDescriptorVersion, GraphicsLibraryImageLayout.NativeMonitorDescriptorCurrentVersion),
                (GraphicsLibraryImageLayout.NativeMonitorDescriptorOwner, 0),
                (GraphicsLibraryImageLayout.NativeMonitorDescriptorAllocation, 0),
                (GraphicsLibraryImageLayout.NativeMonitorDescriptorAllocationSize, 0),
                (GraphicsLibraryImageLayout.NativeMonitorDescriptorId, 0),
                (GraphicsLibraryImageLayout.GfxBaseDefaultMonitor, 0),
                (GraphicsLibraryImageLayout.GfxBaseMonitorListTail, 0)
            })
            {
                Word(0x0CAA); Long(value); Word((ushort)offset);
                Branch(0x6600, targets);
            }
            Word(0x0C6A); Word(0); Word(GraphicsLibraryImageLayout.GfxBaseMonitorListType);
            Branch(0x6600, targets); // list type and pad must both be zero
            Word(0x41EA); Word(GraphicsLibraryImageLayout.GfxBaseMonitorListTail); // LEA tail(A2),A0
            Word(0x2008); Word(0xB0AA); Word(GraphicsLibraryImageLayout.GfxBaseMonitorListHead);
            Branch(0x6600, targets);
            Word(0x41EA); Word(GraphicsLibraryImageLayout.GfxBaseMonitorList);
            Word(0x2008); Word(0xB0AA); Word(GraphicsLibraryImageLayout.GfxBaseMonitorListTailPred);
            Branch(0x6600, targets);
        }
        void RegistrationClaim(List<int> targets)
        {
            NativeGraphicsMonitorRegistrationAdmission.Append(code, targets, ntsc,
                includeMonitorMutation);
            Word(0x4AA9); Word((ushort)NativeGraphicsMonitorRegistrationAdmission.Slot(ntsc));
            Branch(0x6600, targets); // never replace an existing borrowed registration
        }
        Word(0x48E7); Word(0x3020); // MOVEM.L D2-D3/A2,-(SP)
        Word(0x4A80); Branch(0x6700, failures);
        Word(0x0800); Word(0); Branch(0x6600, failures);
        Word(0x0C80); Long(uint.MaxValue - (GraphicsLibraryImageLayout.NativeMonitorImageSize - 1u));
        Branch(0x6200, failures);
        Word(0x2440); // MOVEA.L D0,A2
        InertClaim(failures);
        if (register) RegistrationClaim(failures);
        Word(0x203C); Long((uint)size);
        Word(0x223C); Long(0x10001); // MEMF_PUBLIC|MEMF_CLEAR
        Word(0x4EAE); Word(unchecked((ushort)-198));
        Word(0x4A80); Branch(0x6700, failures);
        Word(0x2600); // retain our candidate in D3 across the recheck
        InertClaim(rollbacks);
        if (register)
        {
            RegistrationClaim(rollbacks);
            Word(0x2409); // retain admitted CMDB in D2 across the initializer
        }
        Word(0x2003); Word(0x223C); Long((uint)size); // D0=candidate,D1=bytes
        Word(0x204A); // MOVEA.L A2,A0: GfxBase backlink
        Word(0x6100); var initialize = code.Count; Word(0);
        Word(0x4A80); Branch(0x6700, rollbacks);
        // Finish the private node before publishing any GfxBase list pointer.
        Word(0x2043); // MOVEA.L D3,A0
        Word(0x43EA); Word(GraphicsLibraryImageLayout.GfxBaseMonitorListTail);
        Word(0x2089); // MOVE.L A1,(A0): successor=tail sentinel
        Word(0x43EA); Word(GraphicsLibraryImageLayout.GfxBaseMonitorList);
        Word(0x2149); Word(4); // MOVE.L A1,4(A0): predecessor=list head
        if (register)
        {
            Word(0x2242); // MOVEA.L D2,A1
            Word(0x2343); Word((ushort)NativeGraphicsMonitorRegistrationAdmission.Slot(ntsc));
        }
        Word(0x200A);
        Word(0x2540); Word(GraphicsLibraryImageLayout.NativeMonitorDescriptorOwner);
        Word(0x2543); Word(GraphicsLibraryImageLayout.NativeMonitorDescriptorAllocation);
        Word(0x203C); Long((uint)size);
        Word(0x2540); Word(GraphicsLibraryImageLayout.NativeMonitorDescriptorAllocationSize);
        Word(0x203C); Long(ntsc ? GraphicsModeIds.NtscMonitor : GraphicsModeIds.PalMonitor);
        Word(0x2540); Word(GraphicsLibraryImageLayout.NativeMonitorDescriptorId);
        foreach (var offset in new[] { GraphicsLibraryImageLayout.GfxBaseMonitorListHead,
            GraphicsLibraryImageLayout.GfxBaseMonitorListTailPred, GraphicsLibraryImageLayout.GfxBaseDefaultMonitor })
        {
            Word(0x2543); Word((ushort)offset);
        }
        Word(0x203C); Long(GraphicsLibraryImageLayout.NativeMonitorDescriptorValidTag);
        Word(0x2540); Word(GraphicsLibraryImageLayout.NativeMonitorDescriptorTag); // valid LAST
        Word(0x200A); Word(0x6000); var success = code.Count; Word(0);
        foreach (var rollback in rollbacks) Patch(rollback, code.Count);
        Word(0x2243); Word(0x203C); Long((uint)size);
        Word(0x4EAE); Word(unchecked((ushort)-210)); // free only our candidate
        foreach (var failure in failures) Patch(failure, code.Count);
        Word(0x7000);
        Patch(success, code.Count);
        Word(0x4CDF); Word(0x040C); Word(0x4E75);
        Patch(initialize, code.Count);
        code.AddRange(initializer);
        return code.ToArray();
    }
}
