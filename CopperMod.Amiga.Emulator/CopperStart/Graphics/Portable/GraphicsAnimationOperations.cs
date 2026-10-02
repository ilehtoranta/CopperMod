using System;
using System.Collections.Generic;
using System.Numerics;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>
/// Guest-side Bob/AnimComp/AnimOb operations.  The implementation keeps the
/// public 68k links, timers, positions, and buffer ownership in guest memory;
/// portable SAVEBOB background snapshots are restored/captured around the
/// provider draw boundary, while copper projection, callback execution, and
/// collision dispatch remain delegated through <see cref="IGraphicsGelsBackend"/>.
/// </summary>
internal static class GraphicsAnimationOperations
{
    internal sealed class Registry
    {
        internal Dictionary<uint, BufferSet> Sets { get; } = new();
    }

    internal sealed class BufferSet
    {
        internal required bool DoubleBuffered { get; init; }
        internal List<BufferAllocation> Allocations { get; } = new();
        internal Dictionary<uint, BobBufferState> Bobs { get; } = new();
    }

    internal sealed class BobBufferState
    {
        internal required uint Bob { get; init; }
        internal required uint VSprite { get; init; }
        internal required uint SaveBuffer { get; init; }
        internal required uint SaveBytes { get; init; }
        internal required uint BorderLine { get; init; }
        internal required uint BorderBytes { get; init; }
        internal required uint CollMask { get; init; }
        internal required uint CollBytes { get; init; }
        internal required uint ImageShadow { get; init; }
        internal required uint ImageShadowBytes { get; init; }
        internal uint DBufPacket { get; init; }
        internal uint DBufPacketBytes { get; init; }
        internal uint DBufBuffer { get; init; }
        internal uint DBufBufferBytes { get; init; }
    }

    internal sealed record BufferAllocation(
        uint Address,
        uint Bytes,
        GraphicsMemoryClass MemoryClass);

    internal static bool AddBob(IGraphicsMemory memory, uint bob, uint rastPort)
    {
        if (!TryGetBob(memory, bob, out var vSprite) ||
            !ProbeRange(memory, rastPort, GraphicsLayouts.RastPortMinimumSize) ||
            !ProbeRange(memory, vSprite, GraphicsLayouts.VSpriteSize) ||
            !memory.TryReadLong(vSprite + (uint)GraphicsLayouts.VSpriteVSBob, out var owner) ||
            (owner != 0 && owner != bob) ||
            !memory.TryReadWord(bob + (uint)GraphicsLayouts.BobFlags, out var flags) ||
            !memory.TryReadWord(vSprite + (uint)GraphicsLayouts.VSpriteFlags, out var vSpriteFlags))
        {
            return false;
        }

        // AddBob starts a fresh system admission pass.  The classic Bob
        // contract preserves only the caller-owned low byte and marks the
        // Bob waiting; drawn/away/nix and double-buffer status are rebuilt by
        // the subsequent GELS passes.
        var nextFlags = (ushort)((flags & 0x00FF) | GraphicsLayouts.BobFlagWaiting);
        var nextVSpriteFlags = (ushort)(vSpriteFlags & ~GraphicsLayouts.VSpriteFlag);

        // A Bob is represented by a VSprite envelope, but it must not be
        // mistaken for a hardware sprite by AddVSprite/copper projection.
        if (!memory.TryWriteWord(
                bob + (uint)GraphicsLayouts.BobFlags,
                nextFlags) ||
            !memory.TryWriteWord(
                vSprite + (uint)GraphicsLayouts.VSpriteFlags,
                nextVSpriteFlags) ||
            !memory.TryWriteLong(
                vSprite + (uint)GraphicsLayouts.VSpriteVSBob,
                bob))
        {
            RestoreWord(memory, bob + (uint)GraphicsLayouts.BobFlags, flags);
            RestoreWord(memory, vSprite + (uint)GraphicsLayouts.VSpriteFlags, vSpriteFlags);
            RestoreLong(memory, vSprite + (uint)GraphicsLayouts.VSpriteVSBob, owner);
            return false;
        }

        if (!GraphicsGelsOperations.AddVSprite(memory, vSprite, rastPort))
        {
            RestoreWord(memory, vSprite + (uint)GraphicsLayouts.VSpriteFlags, vSpriteFlags);
            RestoreWord(memory, bob + (uint)GraphicsLayouts.BobFlags, flags);
            RestoreLong(memory, vSprite + (uint)GraphicsLayouts.VSpriteVSBob, owner);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Resolves every guest field AddBob publishes before it delegates to the
    /// VSprite list owner: Bob flags, the VSprite ownership/flag fields, and
    /// the reciprocal insertion links.  Native-overlay callers use this
    /// structural pass to avoid partially admitting a provider-owned Bob.
    /// </summary>
    internal static bool TryGetAddBobPublicationSpans(
        IGraphicsMemory memory,
        uint bob,
        uint rastPort,
        out List<(uint Address, ulong Bytes)> spans)
    {
        spans = new List<(uint Address, ulong Bytes)>();
        if (!TryGetBob(memory, bob, out var vSprite) ||
            !ProbeRange(memory, rastPort, GraphicsLayouts.RastPortMinimumSize) ||
            !ProbeRange(memory, vSprite, GraphicsLayouts.VSpriteSize) ||
            !memory.TryReadLong(
                vSprite + (uint)GraphicsLayouts.VSpriteVSBob,
                out var owner) ||
            (owner != 0 && owner != bob) ||
            !memory.TryReadWord(
                bob + (uint)GraphicsLayouts.BobFlags,
                out _) ||
            !memory.TryReadWord(
                vSprite + (uint)GraphicsLayouts.VSpriteFlags,
                out _))
        {
            return false;
        }

        if (!GraphicsGelsOperations.TryGetAddVSpritePublicationSpans(
                memory,
                vSprite,
                rastPort,
                out var listSpans))
        {
            return false;
        }

        spans.Add((bob + (uint)GraphicsLayouts.BobFlags, sizeof(ushort)));
        spans.Add((vSprite + (uint)GraphicsLayouts.VSpriteFlags, sizeof(ushort)));
        spans.Add((vSprite + (uint)GraphicsLayouts.VSpriteVSBob, sizeof(uint)));
        spans.AddRange(listSpans);
        return true;
    }

    internal static bool RemIBob(
        IGraphicsMemory memory,
        uint bob,
        uint rastPort,
        uint viewPort,
        IGraphicsGelsBackend? backend)
    {
        if (!TryGetBob(memory, bob, out var vSprite) ||
            !ProbeRange(memory, rastPort, GraphicsLayouts.RastPortMinimumSize) ||
            (viewPort != 0 && !ProbeRange(memory, viewPort, GraphicsLayouts.ViewPortSize)))
        {
            return false;
        }

        if (!TryReadLong(memory, vSprite + (uint)GraphicsLayouts.VSpriteNext, out var next) ||
            !TryReadLong(memory, vSprite + (uint)GraphicsLayouts.VSpritePrev, out var previous) ||
            !memory.TryReadWord(bob + (uint)GraphicsLayouts.BobFlags, out var originalFlags) ||
            !memory.TryReadLong(
                vSprite + (uint)GraphicsLayouts.VSpriteVSBob,
                out var originalOwner))
        {
            return false;
        }

        // RemIBob is intentionally idempotent for an already removed Bob.
        if ((next == 0) != (previous == 0))
            return false;

        uint originalPreviousNext = 0;
        uint originalNextPrevious = 0;
        if (next != 0 &&
            (!memory.TryReadLong(
                 previous + (uint)GraphicsLayouts.VSpriteNext,
                 out originalPreviousNext) ||
             !memory.TryReadLong(
                 next + (uint)GraphicsLayouts.VSpritePrev,
                 out originalNextPrevious) ||
             originalPreviousNext != vSprite ||
             originalNextPrevious != vSprite))
        {
            return false;
        }

        // A Bob with SAVEBOB owns the background currently held in its save
        // buffer. Restore that guest raster before unlinking it so removing a
        // Bob cannot leave its previous image painted into the RastPort.
        if (!TryRestoreBobBackground(memory, bob, rastPort))
            return false;

        if (next != 0 && !GraphicsGelsOperations.RemVSprite(memory, vSprite))
            return false;

        // Immediate removal leaves the caller-owned Bob mode bits intact but
        // clears every system-owned admission/draw/teardown status bit.
        var nextFlags = (ushort)(originalFlags & 0x00FF);
        if (!memory.TryWriteWord(
                bob + (uint)GraphicsLayouts.BobFlags,
                nextFlags) ||
            !memory.TryWriteLong(vSprite + (uint)GraphicsLayouts.VSpriteVSBob, 0))
        {
            RestoreWord(
                memory,
                bob + (uint)GraphicsLayouts.BobFlags,
                originalFlags);
            RestoreLong(
                memory,
                vSprite + (uint)GraphicsLayouts.VSpriteVSBob,
                originalOwner);
            if (next != 0)
            {
                RestoreLong(
                    memory,
                    previous + (uint)GraphicsLayouts.VSpriteNext,
                    originalPreviousNext);
                RestoreLong(
                    memory,
                    next + (uint)GraphicsLayouts.VSpritePrev,
                    originalNextPrevious);
                RestoreLong(
                    memory,
                    vSprite + (uint)GraphicsLayouts.VSpriteNext,
                    next);
                RestoreLong(
                    memory,
                    vSprite + (uint)GraphicsLayouts.VSpritePrev,
                    previous);
            }
            return false;
        }

        backend?.RemIBob(bob, rastPort, viewPort);
        return true;
    }

    /// <summary>
    /// Resolves every guest destination RemIBob may touch: optional SAVEBOB
    /// restore rows, reciprocal VSprite unlink links, Bob flags, and the
    /// VSBob owner LONG.  The native overlay uses this bounded pass before
    /// claiming a guest-only teardown; provider-backed GELS remains an
    /// explicit adapter boundary.
    /// </summary>
    internal static bool TryGetRemIBobPublicationSpans(
        IGraphicsMemory memory,
        uint bob,
        uint rastPort,
        uint viewPort,
        out List<(uint Address, ulong Bytes)> spans)
    {
        spans = new List<(uint Address, ulong Bytes)>();
        if (!TryGetBob(memory, bob, out var vSprite) ||
            !ProbeRange(memory, rastPort, GraphicsLayouts.RastPortMinimumSize) ||
            (viewPort != 0 && !ProbeRange(memory, viewPort, GraphicsLayouts.ViewPortSize)) ||
            !TryReadLong(
                memory,
                vSprite + (uint)GraphicsLayouts.VSpriteNext,
                out var next) ||
            !TryReadLong(
                memory,
                vSprite + (uint)GraphicsLayouts.VSpritePrev,
                out var previous) ||
            !memory.TryReadWord(
                bob + (uint)GraphicsLayouts.BobFlags,
                out _) ||
            !memory.TryReadLong(
                vSprite + (uint)GraphicsLayouts.VSpriteVSBob,
                out _))
        {
            return false;
        }

        if ((next == 0) != (previous == 0))
            return false;

        if (next != 0 &&
            (!memory.TryReadLong(
                 previous + (uint)GraphicsLayouts.VSpriteNext,
                 out var previousNext) ||
             !memory.TryReadLong(
                 next + (uint)GraphicsLayouts.VSpritePrev,
                 out var nextPrevious) ||
             previousNext != vSprite ||
             nextPrevious != vSprite))
        {
            return false;
        }

        if (!memory.TryReadWord(
                vSprite + (uint)GraphicsLayouts.VSpriteFlags,
                out var vSpriteFlags))
        {
            return false;
        }

        if ((vSpriteFlags & GraphicsLayouts.VSpriteBackSaved) != 0)
        {
            if (!GraphicsRasterOperations.TryReadBitmap(
                    memory,
                    rastPort,
                    out var bitmap) ||
                !TryCreateBobRasterRegion(
                    memory,
                    bitmap,
                    rastPort,
                    bob,
                    out var region) ||
                !TryAppendBobRasterRows(
                    memory,
                    region,
                    restore: true,
                    spans))
            {
                return false;
            }

            spans.Add((
                vSprite + (uint)GraphicsLayouts.VSpriteFlags,
                sizeof(ushort)));
        }

        var listSpans = new List<(uint Address, ulong Bytes)>();
        if (next != 0 &&
            !GraphicsGelsOperations.TryGetRemVSpritePublicationSpans(
                memory,
                vSprite,
                out listSpans))
        {
            return false;
        }

        if (next != 0)
            spans.AddRange(listSpans);

        spans.Add((bob + (uint)GraphicsLayouts.BobFlags, sizeof(ushort)));
        spans.Add((vSprite + (uint)GraphicsLayouts.VSpriteVSBob, sizeof(uint)));
        return true;
    }

    internal static bool AddAnimOb(
        IGraphicsMemory memory,
        uint animOb,
        uint animKeyAddress,
        uint rastPort)
    {
        if (!ProbeRange(memory, animOb, GraphicsLayouts.AnimObSize) ||
            !ProbeRange(memory, animKeyAddress, 4) ||
            !ProbeRange(memory, rastPort, GraphicsLayouts.RastPortMinimumSize) ||
            !memory.TryReadLong(animKeyAddress, out var oldHead) ||
            !memory.TryReadLong(animOb + (uint)GraphicsLayouts.AnimObNextOb, out var oldNext) ||
            !memory.TryReadLong(animOb + (uint)GraphicsLayouts.AnimObPrevOb, out var oldPrev) ||
            oldNext != 0 || oldPrev != 0 || oldHead == animOb ||
            (oldHead != 0 && !ProbeRange(memory, oldHead, GraphicsLayouts.AnimObSize)))
        {
            return false;
        }

        if (!CollectActiveComponents(memory, animOb, out var components))
            return false;

        var originalPublication = new List<(uint Address, uint Value)>();
        if (!TrySnapshotPointer(
                memory,
                animOb + (uint)GraphicsLayouts.AnimObNextOb,
                originalPublication) ||
            !TrySnapshotPointer(
                memory,
                animOb + (uint)GraphicsLayouts.AnimObPrevOb,
                originalPublication) ||
            (oldHead != 0 &&
             !TrySnapshotPointer(
                 memory,
                 oldHead + (uint)GraphicsLayouts.AnimObPrevOb,
                 originalPublication)) ||
            !TrySnapshotPointer(memory, animKeyAddress, originalPublication))
        {
            return false;
        }

        var originalComponents = new List<(uint Address, uint Value)>();
        foreach (var component in components)
        {
            if (!TrySnapshotPointer(
                    memory,
                    component + (uint)GraphicsLayouts.AnimCompTimer,
                    originalComponents) ||
                !TrySnapshotPointer(
                    memory,
                    component + (uint)GraphicsLayouts.AnimCompHeadOb,
                    originalComponents))
            {
                return false;
            }
        }

        var bobs = new List<uint>();
        foreach (var component in components)
        {
            if (!memory.TryReadLong(component + (uint)GraphicsLayouts.AnimCompAnimBob, out var bob) ||
                bob == 0)
                continue;

            if (!bobs.Contains(bob))
                bobs.Add(bob);

            if (!TryGetBob(memory, bob, out _))
                return false;
        }

        if (!memory.TryWriteLong(animOb + (uint)GraphicsLayouts.AnimObNextOb, oldHead) ||
            !memory.TryWriteLong(animOb + (uint)GraphicsLayouts.AnimObPrevOb, 0) ||
            (oldHead != 0 && !memory.TryWriteLong(
                oldHead + (uint)GraphicsLayouts.AnimObPrevOb,
                animOb)) ||
                !memory.TryWriteLong(animKeyAddress, animOb))
        {
            RestorePointers(memory, originalPublication);
            return false;
        }

        var added = new List<uint>();
        foreach (var component in components)
        {
            if (!memory.TryReadWord(component + (uint)GraphicsLayouts.AnimCompTimeSet, out var timeSet) ||
                !memory.TryWriteWord(component + (uint)GraphicsLayouts.AnimCompTimer, timeSet) ||
                !memory.TryWriteLong(component + (uint)GraphicsLayouts.AnimCompHeadOb, animOb))
            {
                RollbackAnimOb(memory, originalPublication, originalComponents, added);
                return false;
            }

            if (!memory.TryReadLong(component + (uint)GraphicsLayouts.AnimCompAnimBob, out var bob) ||
                bob == 0 || added.Contains(bob))
                continue;

            if (!AddBob(memory, bob, rastPort))
            {
                RollbackAnimOb(memory, originalPublication, originalComponents, added);
                return false;
            }

            added.Add(bob);
        }

        return true;
    }

    /// <summary>
    /// Resolves the public envelopes touched by AddAnimOb before it links the
    /// AnimOb, initializes active components, and delegates each distinct Bob
    /// through AddBob.  Full validated envelopes are intentional here: the
    /// delegated insertion destination depends on prior Bobs in the same
    /// call, so the existing GELS-chain link set and every participating
    /// Bob/VSprite envelope are admitted as one transaction.
    /// </summary>
    internal static bool TryGetAddAnimObPublicationSpans(
        IGraphicsMemory memory,
        uint animOb,
        uint animKeyAddress,
        uint rastPort,
        out List<(uint Address, ulong Bytes)> spans)
    {
        spans = new List<(uint Address, ulong Bytes)>();
        if (!ProbeRange(memory, animOb, GraphicsLayouts.AnimObSize) ||
            !ProbeRange(memory, animKeyAddress, sizeof(uint)) ||
            !ProbeRange(memory, rastPort, GraphicsLayouts.RastPortMinimumSize) ||
            !memory.TryReadLong(animKeyAddress, out var oldHead) ||
            !memory.TryReadLong(
                animOb + (uint)GraphicsLayouts.AnimObNextOb,
                out var oldNext) ||
            !memory.TryReadLong(
                animOb + (uint)GraphicsLayouts.AnimObPrevOb,
                out var oldPrev) ||
            oldNext != 0 || oldPrev != 0 || oldHead == animOb ||
            (oldHead != 0 &&
             !ProbeRange(memory, oldHead, GraphicsLayouts.AnimObSize)) ||
            !CollectActiveComponents(memory, animOb, out var components))
        {
            return false;
        }

        spans.Add((animOb, (ulong)GraphicsLayouts.AnimObSize));
        spans.Add((animKeyAddress, sizeof(uint)));
        if (oldHead != 0)
            spans.Add((oldHead, (ulong)GraphicsLayouts.AnimObSize));

        var bobs = new HashSet<uint>();
        foreach (var component in components)
        {
            spans.Add((component, (ulong)GraphicsLayouts.AnimCompSize));
            if (!memory.TryReadLong(
                    component + (uint)GraphicsLayouts.AnimCompAnimBob,
                    out var bob) ||
                bob == 0)
            {
                continue;
            }

            if (!bobs.Add(bob))
                continue;

            if (!TryGetBob(memory, bob, out var vSprite) ||
                !memory.TryReadLong(
                    vSprite + (uint)GraphicsLayouts.VSpriteVSBob,
                    out var owner) ||
                (owner != 0 && owner != bob))
            {
                return false;
            }

            spans.Add((bob, (ulong)GraphicsLayouts.BobSize));
            spans.Add((vSprite, (ulong)GraphicsLayouts.VSpriteSize));
        }

        if (bobs.Count != 0)
        {
            if (!GraphicsGelsOperations.TryGetSortGListPublicationSpans(
                    memory,
                    rastPort,
                    out var listSpans))
            {
                return false;
            }

            spans.AddRange(listSpans);
        }

        return true;
    }

    internal static bool Animate(
        IGraphicsMemory memory,
        uint animKeyAddress,
        uint rastPort)
    {
        if (!ProbeRange(memory, animKeyAddress, 4) ||
            !ProbeRange(memory, rastPort, GraphicsLayouts.RastPortMinimumSize) ||
            !memory.TryReadLong(animKeyAddress, out var current))
        {
            return false;
        }

        var visited = new HashSet<uint>();
        for (var count = 0; current != 0 && count < 4096; count++)
        {
            if (!visited.Add(current) ||
                !ProbeRange(memory, current, GraphicsLayouts.AnimObSize) ||
                !AdvanceAnimOb(memory, current))
            {
                return false;
            }

            if (!CollectActiveComponents(memory, current, out var components))
                return false;

            foreach (var component in components)
            {
                if (!UpdateComponent(memory, current, component, rastPort))
                    return false;
            }

            if (!memory.TryReadLong(current + (uint)GraphicsLayouts.AnimObNextOb, out current))
                return false;
        }

        return current == 0;
    }

    /// <summary>
    /// Resolves the guest destinations touched by one Animate traversal before
    /// native-overlay dispatch.  The walk includes every linked AnimOb, all
    /// active/sequence components, distinct Bob/VSprite envelopes, current
    /// GELS link fields needed by sequence switches, and any SAVEBOB rows that
    /// an old Bob can restore during a switch.
    /// </summary>
    internal static bool TryGetAnimatePublicationSpans(
        IGraphicsMemory memory,
        uint animKeyAddress,
        uint rastPort,
        out List<(uint Address, ulong Bytes)> spans)
    {
        spans = new List<(uint Address, ulong Bytes)>();
        if (!ProbeRange(memory, animKeyAddress, sizeof(uint)) ||
            !ProbeRange(memory, rastPort, GraphicsLayouts.RastPortMinimumSize) ||
            !memory.TryReadLong(animKeyAddress, out var current))
        {
            return false;
        }

        var visitedAnimObjects = new HashSet<uint>();
        var bobs = new HashSet<uint>();
        for (var count = 0; current != 0 && count < 4096; count++)
        {
            if (!visitedAnimObjects.Add(current) ||
                !ProbeRange(memory, current, GraphicsLayouts.AnimObSize) ||
                !CollectAllComponents(memory, current, out var components))
            {
                return false;
            }

            spans.Add((current, (ulong)GraphicsLayouts.AnimObSize));
            foreach (var component in components)
            {
                spans.Add((component, (ulong)GraphicsLayouts.AnimCompSize));
                if (!memory.TryReadLong(
                        component + (uint)GraphicsLayouts.AnimCompAnimBob,
                        out var bob) ||
                    bob == 0 ||
                    !bobs.Add(bob))
                {
                    continue;
                }

                if (!TryGetBob(memory, bob, out var vSprite))
                    return false;

                spans.Add((bob, (ulong)GraphicsLayouts.BobSize));
                spans.Add((vSprite, (ulong)GraphicsLayouts.VSpriteSize));
            }

            if (!memory.TryReadLong(
                    current + (uint)GraphicsLayouts.AnimObNextOb,
                    out current))
            {
                return false;
            }
        }

        if (current != 0)
            return false;

        if (bobs.Count == 0)
            return true;

        if (!GraphicsGelsOperations.TryGetSortGListPublicationSpans(
                memory,
                rastPort,
                out var listSpans))
        {
            return false;
        }

        spans.AddRange(listSpans);
        if (!GraphicsRasterOperations.TryReadBitmap(
                memory,
                rastPort,
                out var bitmap))
        {
            // A normal animation without a SAVEBOB may not expose a bitmap
            // yet.  The current call only needs the link/state envelope in
            // that case; a SAVEBOB path below still requires the bitmap.
            foreach (var bob in bobs)
            {
                if (!TryGetBob(memory, bob, out var vSprite) ||
                    !memory.TryReadWord(
                        vSprite + (uint)GraphicsLayouts.VSpriteFlags,
                        out var flags) ||
                    (flags & GraphicsLayouts.VSpriteBackSaved) != 0)
                {
                    return false;
                }
            }

            return true;
        }

        foreach (var bob in bobs)
        {
            if (!TryGetBob(memory, bob, out var vSprite) ||
                !memory.TryReadWord(
                    vSprite + (uint)GraphicsLayouts.VSpriteFlags,
                    out var flags))
            {
                return false;
            }

            if ((flags & GraphicsLayouts.VSpriteBackSaved) == 0)
                continue;

            if (!TryCreateBobRasterRegion(
                    memory,
                    bitmap,
                    rastPort,
                    bob,
                    out var region) ||
                !TryAppendBobRasterRows(
                    memory,
                    region,
                    restore: true,
                    spans))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool InitGMasks(IGraphicsMemory memory, uint animOb)
    {
        if (!ProbeRange(memory, animOb, GraphicsLayouts.AnimObSize) ||
            !CollectAllComponents(memory, animOb, out var components))
        {
            return false;
        }

        var originalOwners = new List<(uint Address, uint Value)>();
        bool FailInitGMasks()
        {
            RestorePointers(memory, originalOwners);
            return false;
        }

        foreach (var component in components)
        {
            if (!memory.TryReadLong(component + (uint)GraphicsLayouts.AnimCompAnimBob, out var bob) ||
                bob == 0)
                continue;

            if (!TryGetBob(memory, bob, out var vSprite) ||
                !TrySnapshotPointer(
                    memory,
                    vSprite + (uint)GraphicsLayouts.VSpriteVSBob,
                    originalOwners) ||
                !memory.TryWriteLong(
                    vSprite + (uint)GraphicsLayouts.VSpriteVSBob,
                    bob) ||
                !GraphicsGelsOperations.InitMasks(memory, vSprite))
                return FailInitGMasks();
        }

        return true;
    }

    /// <summary>
    /// Describes the caller-owned VSprite fields and mask buffers that
    /// InitGMasks publishes before the portable mask builder writes any words.
    /// The native overlay uses this structural pass to make its ownership
    /// decision before invoking a provider/image mapping that may be read-only;
    /// the actual pointer-and-mask transaction remains in <see cref="InitGMasks"/>.
    /// </summary>
    internal static bool TryGetInitGMasksPublicationSpans(
        IGraphicsMemory memory,
        uint animOb,
        out List<(uint Address, ulong Bytes)> spans)
    {
        spans = new List<(uint Address, ulong Bytes)>();
        if (!ProbeRange(memory, animOb, GraphicsLayouts.AnimObSize) ||
            !CollectAllComponents(memory, animOb, out var components))
        {
            return false;
        }

        var bobs = new HashSet<uint>();
        foreach (var component in components)
        {
            if (!memory.TryReadLong(
                    component + (uint)GraphicsLayouts.AnimCompAnimBob,
                    out var bob) ||
                bob == 0 ||
                !bobs.Add(bob))
            {
                continue;
            }

            if (!TryGetBob(memory, bob, out var vSprite) ||
                !memory.TryReadWord(
                    vSprite + (uint)GraphicsLayouts.VSpriteFlags,
                    out var flags) ||
                !memory.TryReadWord(
                    vSprite + (uint)GraphicsLayouts.VSpriteHeight,
                    out var height) ||
                !memory.TryReadWord(
                    vSprite + (uint)GraphicsLayouts.VSpriteWidth,
                    out var width) ||
                !memory.TryReadWord(
                    vSprite + (uint)GraphicsLayouts.VSpriteDepth,
                    out var depth) ||
                !memory.TryReadLong(
                    vSprite + (uint)GraphicsLayouts.VSpriteImageData,
                    out var imageData) ||
                !memory.TryReadLong(
                    vSprite + (uint)GraphicsLayouts.VSpriteBorderLine,
                    out var borderLine) ||
                !memory.TryReadLong(
                    vSprite + (uint)GraphicsLayouts.VSpriteCollMask,
                    out var collMask) ||
                height == 0 || width == 0 || depth == 0 ||
                imageData == 0 || borderLine == 0)
            {
                return false;
            }

            if ((flags & GraphicsLayouts.VSpriteFlag) == 0 &&
                collMask == 0 &&
                (!memory.TryReadLong(
                     bob + (uint)GraphicsLayouts.BobImageShadow,
                     out collMask) ||
                 collMask == 0))
            {
                return false;
            }

            if (collMask == 0)
                return false;

            var planeWords = (ulong)height * width;
            var imageWords = planeWords * depth;
            var borderBytes = (ulong)width * 2ul;
            var collBytes = planeWords * 2ul;
            if (planeWords > int.MaxValue || imageWords > int.MaxValue ||
                borderBytes > uint.MaxValue || collBytes > uint.MaxValue ||
                imageWords > int.MaxValue / 2 ||
                borderBytes > int.MaxValue || collBytes > int.MaxValue ||
                !ProbeRange(memory, imageData, checked((int)(imageWords * 2ul))) ||
                !ProbeRange(memory, borderLine, checked((int)borderBytes)) ||
                !ProbeRange(memory, collMask, checked((int)collBytes)))
            {
                return false;
            }

            spans.Add((
                vSprite + (uint)GraphicsLayouts.VSpriteVSBob,
                sizeof(uint)));
            spans.Add((borderLine, borderBytes));
            spans.Add((collMask, collBytes));
        }

        return true;
    }

    internal static bool GetGBuffers(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend allocator,
        Registry registry,
        uint animOb,
        uint rastPort,
        uint db)
    {
        if (!ProbeRange(memory, animOb, GraphicsLayouts.AnimObSize) ||
            !ProbeRange(memory, rastPort, GraphicsLayouts.RastPortMinimumSize) ||
            registry.Sets.ContainsKey(animOb) ||
            !memory.TryReadLong(rastPort + (uint)GraphicsLayouts.RastPortBitMap, out var bitMap) ||
            !ProbeRange(
                memory,
                bitMap,
                GraphicsLayouts.BitMapDepth + sizeof(byte)) ||
            !memory.TryReadWord(bitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, out var bytesPerRow) ||
            !memory.TryReadWord(bitMap + (uint)GraphicsLayouts.BitMapRows, out var rows) ||
            !memory.TryReadByte(bitMap + (uint)GraphicsLayouts.BitMapDepth, out var depth) ||
            bytesPerRow == 0 || rows == 0 || depth == 0 ||
            !CollectAllComponents(memory, animOb, out var components))
        {
            return false;
        }

        // GetGBuffers only needs bitmap geometry, but the native BitMap
        // header still ends after the declared plane links when BMF_MINPLANES
        // is used.  Do not require an unmapped eight-plane tail.
        var structureBytes = GraphicsLayouts.BitMapPlanes + (depth * sizeof(uint));
        if (!ProbeRange(memory, bitMap, structureBytes))
            return false;

        var set = new BufferSet { DoubleBuffered = db != 0 };
        foreach (var component in components)
        {
            if (!memory.TryReadLong(component + (uint)GraphicsLayouts.AnimCompAnimBob, out var bob) ||
                bob == 0 || set.Bobs.ContainsKey(bob))
                continue;
            if (!TryGetBob(memory, bob, out var vSprite) ||
                !memory.TryReadWord(vSprite + (uint)GraphicsLayouts.VSpriteWidth, out var widthWords) ||
                !memory.TryReadWord(vSprite + (uint)GraphicsLayouts.VSpriteHeight, out var height) ||
                widthWords == 0 || height == 0)
            {
                FreeSet(allocator, set);
                return false;
            }

            var saveBytes = (ulong)bytesPerRow * rows * depth;
            var maskBytes = (ulong)widthWords * height * 2ul;
            var borderBytes = (ulong)widthWords * 2ul;
            if (!TrySize(saveBytes, out var save) ||
                !TrySize(maskBytes, out var mask) ||
                !TrySize(borderBytes, out var border) ||
                !Allocate(allocator, set, save, GraphicsMemoryClass.Chip, out var saveAddress) ||
                !Allocate(allocator, set, border, GraphicsMemoryClass.Chip, out var borderAddress) ||
                !Allocate(allocator, set, mask, GraphicsMemoryClass.Chip, out var collAddress) ||
                !Allocate(allocator, set, mask, GraphicsMemoryClass.Chip, out var shadowAddress))
            {
                FreeSet(allocator, set);
                return false;
            }

            uint packetAddress = 0;
            uint bufferAddress = 0;
            if (db != 0 &&
                (!Allocate(allocator, set, (uint)GraphicsLayouts.DBufPacketSize, GraphicsMemoryClass.Public, out packetAddress) ||
                 !Allocate(allocator, set, save, GraphicsMemoryClass.Chip, out bufferAddress)))
            {
                FreeSet(allocator, set);
                return false;
            }

            var state = new BobBufferState
            {
                Bob = bob,
                VSprite = vSprite,
                SaveBuffer = saveAddress,
                SaveBytes = save,
                BorderLine = borderAddress,
                BorderBytes = border,
                CollMask = collAddress,
                CollBytes = mask,
                ImageShadow = shadowAddress,
                ImageShadowBytes = mask,
                DBufPacket = packetAddress,
                DBufPacketBytes = packetAddress == 0 ? 0u : (uint)GraphicsLayouts.DBufPacketSize,
                DBufBuffer = bufferAddress,
                DBufBufferBytes = bufferAddress == 0 ? 0u : save
            };
            set.Bobs.Add(bob, state);
        }

        // GetGBuffers publishes five guest-visible pointers per Bob/VSprite
        // only after all storage has been allocated.  Keep the old values so
        // an allocator-backed setup can be rolled back without leaving stale
        // pointers to storage that is about to be freed.
        var originalPointers = new List<(uint Address, uint Value)>();
        foreach (var state in set.Bobs.Values)
        {
            if (!TrySnapshotPointer(
                    memory,
                    state.Bob + (uint)GraphicsLayouts.BobSaveBuffer,
                    originalPointers) ||
                !TrySnapshotPointer(
                    memory,
                    state.Bob + (uint)GraphicsLayouts.BobImageShadow,
                    originalPointers) ||
                !TrySnapshotPointer(
                    memory,
                    state.Bob + (uint)GraphicsLayouts.BobDBuffer,
                    originalPointers) ||
                !TrySnapshotPointer(
                    memory,
                    state.VSprite + (uint)GraphicsLayouts.VSpriteBorderLine,
                    originalPointers) ||
                !TrySnapshotPointer(
                    memory,
                    state.VSprite + (uint)GraphicsLayouts.VSpriteCollMask,
                    originalPointers))
            {
                FreeSet(allocator, set);
                return false;
            }
        }

        bool FailGetGBuffers()
        {
            RestorePointers(memory, originalPointers);
            FreeSet(allocator, set);
            return false;
        }

        foreach (var allocation in set.Allocations)
        {
            if (!ClearRange(memory, allocation.Address, allocation.Bytes))
                return FailGetGBuffers();
        }

        foreach (var state in set.Bobs.Values)
        {
            if (!memory.TryWriteLong(state.Bob + (uint)GraphicsLayouts.BobSaveBuffer, state.SaveBuffer) ||
                !memory.TryWriteLong(state.Bob + (uint)GraphicsLayouts.BobImageShadow, state.ImageShadow) ||
                !memory.TryWriteLong(state.Bob + (uint)GraphicsLayouts.BobDBuffer, state.DBufPacket) ||
                !memory.TryWriteLong(state.VSprite + (uint)GraphicsLayouts.VSpriteBorderLine, state.BorderLine) ||
                !memory.TryWriteLong(state.VSprite + (uint)GraphicsLayouts.VSpriteCollMask, state.CollMask))
                return FailGetGBuffers();

            if (state.DBufPacket != 0 &&
                (!memory.TryWriteWord(state.DBufPacket + (uint)GraphicsLayouts.DBufPacketY, 0) ||
                 !memory.TryWriteWord(state.DBufPacket + (uint)GraphicsLayouts.DBufPacketX, 0) ||
                 !memory.TryWriteLong(state.DBufPacket + (uint)GraphicsLayouts.DBufPacketPath, 0) ||
                 !memory.TryWriteLong(state.DBufPacket + (uint)GraphicsLayouts.DBufPacketBuffer, state.DBufBuffer) ||
                 !memory.TryWriteLong(state.DBufPacket + (uint)GraphicsLayouts.DBufPacketPlanes, 0)))
                return FailGetGBuffers();
        }

        registry.Sets.Add(animOb, set);
        return true;
    }

    /// <summary>
    /// Describes the caller-owned Bob/VSprite LONGs that GetGBuffers
    /// publishes before allocating its private save/mask storage.  The native
    /// overlay uses this read-only structural pass to make the ownership
    /// decision before invoking an allocator; the actual allocator-backed
    /// transaction remains in <see cref="GetGBuffers"/>.
    /// </summary>
    internal static bool TryGetGBuffersPublicationSpans(
        IGraphicsMemory memory,
        Registry registry,
        uint animOb,
        uint rastPort,
        out List<uint> addresses)
    {
        addresses = new List<uint>();
        if (!ProbeRange(memory, animOb, GraphicsLayouts.AnimObSize) ||
            !ProbeRange(memory, rastPort, GraphicsLayouts.RastPortMinimumSize) ||
            registry.Sets.ContainsKey(animOb) ||
            !memory.TryReadLong(
                rastPort + (uint)GraphicsLayouts.RastPortBitMap,
                out var bitMap) ||
            !ProbeRange(
                memory,
                bitMap,
                GraphicsLayouts.BitMapDepth + sizeof(byte)) ||
            !memory.TryReadWord(
                bitMap + (uint)GraphicsLayouts.BitMapBytesPerRow,
                out var bytesPerRow) ||
            !memory.TryReadWord(
                bitMap + (uint)GraphicsLayouts.BitMapRows,
                out var rows) ||
            !memory.TryReadByte(
                bitMap + (uint)GraphicsLayouts.BitMapDepth,
                out var depth) ||
            bytesPerRow == 0 || rows == 0 || depth == 0 ||
            !CollectAllComponents(memory, animOb, out var components))
        {
            return false;
        }

        var structureBytes = GraphicsLayouts.BitMapPlanes +
            (depth * sizeof(uint));
        if (!ProbeRange(memory, bitMap, structureBytes))
            return false;

        var bobs = new HashSet<uint>();
        foreach (var component in components)
        {
            if (!memory.TryReadLong(
                    component + (uint)GraphicsLayouts.AnimCompAnimBob,
                    out var bob) ||
                bob == 0 ||
                !bobs.Add(bob))
            {
                continue;
            }

            if (!TryGetBob(
                    memory,
                    bob,
                    out var vSprite) ||
                !memory.TryReadWord(
                    vSprite + (uint)GraphicsLayouts.VSpriteWidth,
                    out var widthWords) ||
                !memory.TryReadWord(
                    vSprite + (uint)GraphicsLayouts.VSpriteHeight,
                    out var height) ||
                widthWords == 0 || height == 0)
            {
                return false;
            }

            addresses.Add(bob + (uint)GraphicsLayouts.BobSaveBuffer);
            addresses.Add(bob + (uint)GraphicsLayouts.BobImageShadow);
            addresses.Add(bob + (uint)GraphicsLayouts.BobDBuffer);
            addresses.Add(vSprite + (uint)GraphicsLayouts.VSpriteBorderLine);
            addresses.Add(vSprite + (uint)GraphicsLayouts.VSpriteCollMask);
        }

        foreach (var address in addresses)
        {
            if (!ProbeRange(memory, address, sizeof(uint)))
                return false;
        }

        return true;
    }

    internal static bool FreeGBuffers(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend allocator,
        Registry registry,
        uint animOb,
        uint rastPort,
        uint db)
    {
        if (!ProbeRange(memory, animOb, GraphicsLayouts.AnimObSize) ||
            !ProbeRange(memory, rastPort, GraphicsLayouts.RastPortMinimumSize) ||
            !registry.Sets.TryGetValue(animOb, out var set) ||
            set.DoubleBuffered != (db != 0))
        {
            return false;
        }

        // FreeGBuffers is also a teardown boundary for an on-screen SAVEBOB:
        // restore every live background before releasing the storage that
        // backs it. A stale pointer must never be left in the guest raster.
        foreach (var state in set.Bobs.Values)
        {
            if (!TryRestoreBobBackground(memory, state.Bob, rastPort))
                return false;
        }

        var ownedPointers = new List<(uint Address, uint Value)>();
        foreach (var state in set.Bobs.Values)
        {
            if (!TrySnapshotOwnedPointer(
                    memory,
                    state.Bob + (uint)GraphicsLayouts.BobSaveBuffer,
                    state.SaveBuffer,
                    ownedPointers) ||
                !TrySnapshotOwnedPointer(
                    memory,
                    state.Bob + (uint)GraphicsLayouts.BobImageShadow,
                    state.ImageShadow,
                    ownedPointers) ||
                !TrySnapshotOwnedPointer(
                    memory,
                    state.Bob + (uint)GraphicsLayouts.BobDBuffer,
                    state.DBufPacket,
                    ownedPointers) ||
                !TrySnapshotOwnedPointer(
                    memory,
                    state.VSprite + (uint)GraphicsLayouts.VSpriteBorderLine,
                    state.BorderLine,
                    ownedPointers) ||
                !TrySnapshotOwnedPointer(
                    memory,
                    state.VSprite + (uint)GraphicsLayouts.VSpriteCollMask,
                    state.CollMask,
                    ownedPointers))
            {
                return false;
            }
        }

        foreach (var pointer in ownedPointers)
        {
            if (memory.TryWriteLong(pointer.Address, 0))
                continue;

            // Keep the registry and allocations live so the caller can retry
            // teardown without exposing a stale pointer to freed storage.
            RestorePointers(memory, ownedPointers);
            return false;
        }

        registry.Sets.Remove(animOb);
        FreeSet(allocator, set);
        return true;
    }

    /// <summary>
    /// Describes the guest-visible destinations that <see cref="FreeGBuffers"/>
    /// may write for an already-owned buffer set.  Teardown can restore a
    /// SAVEBOB into the raster before clearing the five ownership pointers, so
    /// this pass includes those raster rows and the Bob/VSprite flag words as
    /// well as only the pointer fields whose values still match this registry's
    /// allocations.  The native overlay uses the result to reject a read-only
    /// provider mapping before it starts teardown; allocator release remains in
    /// the existing transactional operation.
    /// </summary>
    internal static bool TryGetFreeGBuffersPublicationSpans(
        IGraphicsMemory memory,
        Registry registry,
        uint animOb,
        uint rastPort,
        uint db,
        out List<(uint Address, ulong Bytes)> spans)
    {
        spans = new List<(uint Address, ulong Bytes)>();
        if (!ProbeRange(memory, animOb, GraphicsLayouts.AnimObSize) ||
            !ProbeRange(memory, rastPort, GraphicsLayouts.RastPortMinimumSize) ||
            !registry.Sets.TryGetValue(animOb, out var set) ||
            set.DoubleBuffered != (db != 0))
        {
            return false;
        }

        foreach (var state in set.Bobs.Values)
        {
            // FreeGBuffers' restore helper intentionally treats a missing or
            // malformed Bob as a no-op here; the owned pointer pass below is
            // the authoritative teardown validator.  Mirror that boundary so
            // the native admission helper does not claim a different failure.
            if (TryGetBob(memory, state.Bob, out var vSprite) &&
                memory.TryReadWord(
                    vSprite + (uint)GraphicsLayouts.VSpriteFlags,
                    out var vSpriteFlags) &&
                (vSpriteFlags & GraphicsLayouts.VSpriteBackSaved) != 0)
            {
                if (!GraphicsRasterOperations.TryReadBitmap(
                        memory,
                        rastPort,
                        out var bitmap) ||
                    !TryCreateBobRasterRegion(
                        memory,
                        bitmap,
                        rastPort,
                        state.Bob,
                        out var region))
                {
                    return false;
                }

                var rowBytes = (ulong)region.WidthWords * sizeof(ushort);
                if (rowBytes == 0 || rowBytes > uint.MaxValue)
                    return false;

                var wordX = region.OldX >> 4;
                for (var plane = 0; plane < region.Depth; plane++)
                {
                    var planePointerAddress = (ulong)bitmap.Address +
                        (uint)GraphicsLayouts.BitMapPlanes +
                        (uint)(plane * sizeof(uint));
                    if (planePointerAddress > uint.MaxValue ||
                        !memory.TryReadLong(
                            (uint)planePointerAddress,
                            out var planeAddress) ||
                        planeAddress == 0)
                    {
                        return false;
                    }

                    for (var row = 0; row < region.Height; row++)
                    {
                        var destinationAddress = (ulong)planeAddress +
                            ((ulong)((int)region.OldY + row) *
                             (uint)bitmap.BytesPerRow) +
                            (uint)(wordX * sizeof(ushort));
                        if (destinationAddress > uint.MaxValue ||
                            destinationAddress + rowBytes - 1ul > uint.MaxValue)
                        {
                            return false;
                        }

                        spans.Add(((uint)destinationAddress, rowBytes));
                    }
                }

                spans.Add((
                    region.VSprite + (uint)GraphicsLayouts.VSpriteFlags,
                    sizeof(ushort)));
                spans.Add((
                    region.Bob + (uint)GraphicsLayouts.BobFlags,
                    sizeof(ushort)));
            }

            var pointers = new[]
            {
                (state.Bob + (uint)GraphicsLayouts.BobSaveBuffer,
                    state.SaveBuffer),
                (state.Bob + (uint)GraphicsLayouts.BobImageShadow,
                    state.ImageShadow),
                (state.Bob + (uint)GraphicsLayouts.BobDBuffer,
                    state.DBufPacket),
                (state.VSprite + (uint)GraphicsLayouts.VSpriteBorderLine,
                    state.BorderLine),
                (state.VSprite + (uint)GraphicsLayouts.VSpriteCollMask,
                    state.CollMask)
            };
            foreach (var (address, expected) in pointers)
            {
                if (!memory.TryReadLong(address, out var actual))
                    return false;

                if (expected != 0 && actual == expected)
                    spans.Add((address, sizeof(uint)));
            }
        }

        return true;
    }

    /// <summary>
    /// Describes the guest destinations that <see cref="DrawGList"/> may
    /// publish around the provider draw callback.  A frame can restore a
    /// previous SAVEBOB, capture a new background, and update Bob/VSprite state;
    /// resolve all of those rows and words before a native-overlay call claims
    /// the provider-backed operation.
    /// </summary>
    internal static bool TryGetDrawGListPublicationSpans(
        IGraphicsMemory memory,
        uint rastPort,
        uint viewPort,
        out List<(uint Address, ulong Bytes)> spans)
    {
        spans = new List<(uint Address, ulong Bytes)>();
        if (!ProbeRange(memory, viewPort, GraphicsLayouts.ViewPortSize) ||
            !TryCollectBobRasterRegions(memory, rastPort, out var bobs))
        {
            return false;
        }

        foreach (var bob in bobs)
        {
            if ((bob.VSpriteFlags & GraphicsLayouts.VSpriteBackSaved) != 0)
            {
                if (!TryAppendBobRasterRows(
                        memory,
                        bob,
                        restore: true,
                        spans))
                {
                    return false;
                }

                spans.Add((
                    bob.VSprite + (uint)GraphicsLayouts.VSpriteFlags,
                    sizeof(ushort)));
                spans.Add((
                    bob.Bob + (uint)GraphicsLayouts.BobFlags,
                    sizeof(ushort)));
            }

            if ((bob.VSpriteFlags & GraphicsLayouts.VSpriteSaveBack) != 0)
            {
                if (!TryAppendBobRasterRows(
                        memory,
                        bob,
                        restore: false,
                        spans))
                {
                    return false;
                }

                spans.Add((
                    bob.VSprite + (uint)GraphicsLayouts.VSpriteOldX,
                    sizeof(ushort)));
                spans.Add((
                    bob.VSprite + (uint)GraphicsLayouts.VSpriteOldY,
                    sizeof(ushort)));
                spans.Add((
                    bob.VSprite + (uint)GraphicsLayouts.VSpriteFlags,
                    sizeof(ushort)));
                spans.Add((
                    bob.Bob + (uint)GraphicsLayouts.BobFlags,
                    sizeof(ushort)));
            }
        }

        return true;
    }

    internal static bool DrawGList(
        IGraphicsMemory memory,
        uint rastPort,
        uint viewPort,
        IGraphicsGelsBackend? backend)
    {
        if (!ValidateList(memory, rastPort) ||
            !ProbeRange(memory, viewPort, GraphicsLayouts.ViewPortSize) ||
            backend is null)
            return false;

        if (!TryCollectBobRasterRegions(memory, rastPort, out var bobs))
            return false;

        // Remove the previous frame in reverse z-order, then capture the
        // current background in forward z-order. This is the classic GELS
        // ownership boundary: a host/native backend draws the list only after
        // portable guest save/restore state is coherent.
        for (var index = bobs.Count - 1; index >= 0; index--)
        {
            if ((bobs[index].VSpriteFlags & GraphicsLayouts.VSpriteBackSaved) == 0)
                continue;

            if (!TryRestoreBobBackground(memory, bobs[index]))
                return false;
        }

        foreach (var bob in bobs)
        {
            if ((bob.VSpriteFlags & GraphicsLayouts.VSpriteSaveBack) == 0)
                continue;

            if (!TrySaveBobBackground(memory, bob))
                return false;
        }

        backend.DrawGList(rastPort, viewPort);
        return true;
    }

    private static bool TryAppendBobRasterRows(
        IGraphicsMemory memory,
        BobRasterRegion region,
        bool restore,
        List<(uint Address, ulong Bytes)> spans)
    {
        var rowBytes = (ulong)region.WidthWords * sizeof(ushort);
        if (rowBytes == 0 || rowBytes > uint.MaxValue)
            return false;

        if (!restore)
        {
            for (var plane = 0; plane < region.Depth; plane++)
            {
                for (var row = 0; row < region.Height; row++)
                {
                    var offset =
                        (((ulong)plane * region.Height) + (uint)row) *
                        rowBytes;
                    var address = (ulong)region.SaveBuffer + offset;
                    if (address > uint.MaxValue ||
                        address + rowBytes - 1ul > uint.MaxValue)
                    {
                        return false;
                    }

                    spans.Add(((uint)address, rowBytes));
                }
            }

            return true;
        }

        var wordX = region.OldX >> 4;
        for (var plane = 0; plane < region.Depth; plane++)
        {
            var planePointerAddress = (ulong)region.Bitmap.Address +
                (uint)GraphicsLayouts.BitMapPlanes +
                (uint)(plane * sizeof(uint));
            if (planePointerAddress > uint.MaxValue ||
                !memory.TryReadLong(
                    (uint)planePointerAddress,
                    out var planeAddress) ||
                planeAddress == 0)
            {
                return false;
            }

            for (var row = 0; row < region.Height; row++)
            {
                var address = (ulong)planeAddress +
                    ((ulong)((int)region.OldY + row) *
                     (uint)region.Bitmap.BytesPerRow) +
                    (uint)(wordX * sizeof(ushort));
                if (address > uint.MaxValue ||
                    address + rowBytes - 1ul > uint.MaxValue)
                {
                    return false;
                }

                spans.Add(((uint)address, rowBytes));
            }
        }

        return true;
    }

    internal static bool DoCollision(
        IGraphicsMemory memory,
        uint rastPort,
        IGraphicsGelsBackend? backend)
    {
        if (backend is null ||
            !TryReadGelsList(memory, rastPort, out var list, out var collisionHandler,
                out var leftmost, out var rightmost, out var topmost, out var bottommost))
            return false;

        backend.DoCollision(rastPort);

        if (collisionHandler != 0)
        {
            foreach (var sprite in list)
            {
                if ((sprite.HitMask & 1) == 0)
                    continue;

                ushort boundaryFlags = 0;
                var right = (long)sprite.X + ((long)sprite.Width * 16) - 1;
                var bottom = (long)sprite.Y + sprite.Height - 1;
                if (sprite.Y < topmost)
                    boundaryFlags |= BoundaryTopHit;
                if (bottom > bottommost)
                    boundaryFlags |= BoundaryBottomHit;
                if (sprite.X < leftmost)
                    boundaryFlags |= BoundaryLeftHit;
                if (right > rightmost)
                    boundaryFlags |= BoundaryRightHit;

                if (boundaryFlags != 0 &&
                    TryReadCollisionRoutine(memory, collisionHandler, 0, out var routine))
                {
                    backend.DispatchBoundary(sprite.Address, boundaryFlags, routine);
                }
            }

            for (var firstIndex = 0; firstIndex < list.Count; firstIndex++)
            {
                var first = list[firstIndex];
                for (var secondIndex = firstIndex + 1; secondIndex < list.Count; secondIndex++)
                {
                    var second = list[secondIndex];
                    if (!MasksOverlap(memory, first, second))
                        continue;

                    var collisionBits = (ushort)(first.MeMask & second.HitMask);
                    if (collisionBits == 0)
                        continue;

                    var routineIndex = BitOperations.TrailingZeroCount((uint)collisionBits);
                    if (routineIndex < GraphicsLayouts.CollisionTableEntries &&
                        TryReadCollisionRoutine(
                            memory,
                            collisionHandler,
                            routineIndex,
                            out var routine))
                    {
                        backend.DispatchCollision(first.Address, second.Address, routine);
                    }
                }
            }
        }

        return true;
    }

    private const ushort BoundaryTopHit = 0x0001;
    private const ushort BoundaryBottomHit = 0x0002;
    private const ushort BoundaryLeftHit = 0x0004;
    private const ushort BoundaryRightHit = 0x0008;

    private static bool AdvanceAnimOb(IGraphicsMemory memory, uint animOb)
    {
        if (!TryReadWord(memory, animOb + (uint)GraphicsLayouts.AnimObOldX, out var oldX) ||
            !TryReadWord(memory, animOb + (uint)GraphicsLayouts.AnimObOldY, out var oldY) ||
            !TryReadWord(memory, animOb + (uint)GraphicsLayouts.AnimObX, out var x) ||
            !TryReadWord(memory, animOb + (uint)GraphicsLayouts.AnimObY, out var y) ||
            !TryReadWord(memory, animOb + (uint)GraphicsLayouts.AnimObXVel, out var xVel) ||
            !TryReadWord(memory, animOb + (uint)GraphicsLayouts.AnimObYVel, out var yVel) ||
            !TryReadWord(memory, animOb + (uint)GraphicsLayouts.AnimObXAccel, out var xAccel) ||
            !TryReadWord(memory, animOb + (uint)GraphicsLayouts.AnimObYAccel, out var yAccel) ||
            !TryReadWord(memory, animOb + (uint)GraphicsLayouts.AnimObClock, out var clock))
        {
            return false;
        }

        var original = new[]
        {
            oldX,
            oldY,
            x,
            y,
            xVel,
            yVel,
            clock
        };
        var addresses = new[]
        {
            animOb + (uint)GraphicsLayouts.AnimObOldX,
            animOb + (uint)GraphicsLayouts.AnimObOldY,
            animOb + (uint)GraphicsLayouts.AnimObX,
            animOb + (uint)GraphicsLayouts.AnimObY,
            animOb + (uint)GraphicsLayouts.AnimObXVel,
            animOb + (uint)GraphicsLayouts.AnimObYVel,
            animOb + (uint)GraphicsLayouts.AnimObClock
        };

        if (memory.TryWriteWord(addresses[0], x) &&
            memory.TryWriteWord(addresses[1], y) &&
            memory.TryWriteWord(
                addresses[2],
                unchecked((ushort)((short)x + (short)xVel))) &&
            memory.TryWriteWord(
                addresses[3],
                unchecked((ushort)((short)y + (short)yVel))) &&
            memory.TryWriteWord(
                addresses[4],
                unchecked((ushort)((short)xVel + (short)xAccel))) &&
            memory.TryWriteWord(
                addresses[5],
                unchecked((ushort)((short)yVel + (short)yAccel))) &&
            memory.TryWriteWord(
                addresses[6],
                unchecked((ushort)(clock + 1))))
        {
            return true;
        }

        for (var index = 0; index < addresses.Length; index++)
            RestoreWord(memory, addresses[index], original[index]);
        return false;
    }

    private static bool UpdateComponent(
        IGraphicsMemory memory,
        uint animOb,
        uint component,
        uint rastPort)
    {
        if (!memory.TryReadWord(component + (uint)GraphicsLayouts.AnimCompTimer, out var timer) ||
            !memory.TryReadWord(component + (uint)GraphicsLayouts.AnimCompTimeSet, out var timeSet) ||
            !memory.TryReadWord(component + (uint)GraphicsLayouts.AnimCompXTrans, out var xTrans) ||
            !memory.TryReadWord(component + (uint)GraphicsLayouts.AnimCompYTrans, out var yTrans) ||
            !memory.TryReadWord(animOb + (uint)GraphicsLayouts.AnimObX, out var x) ||
            !memory.TryReadWord(animOb + (uint)GraphicsLayouts.AnimObY, out var y))
        {
            return false;
        }

        var activeComponent = component;
        if (timer != 0)
            timer--;
        else if (timeSet != 0)
        {
            if (!TrySwitchSequence(memory, animOb, component, rastPort, out activeComponent))
                return false;

            if (!memory.TryReadWord(
                    activeComponent + (uint)GraphicsLayouts.AnimCompTimeSet,
                    out timeSet) ||
                !memory.TryReadWord(
                    activeComponent + (uint)GraphicsLayouts.AnimCompXTrans,
                    out xTrans) ||
                !memory.TryReadWord(
                    activeComponent + (uint)GraphicsLayouts.AnimCompYTrans,
                    out yTrans))
            {
                return false;
            }

            timer = timeSet;
        }

        if (!memory.TryReadWord(
                activeComponent + (uint)GraphicsLayouts.AnimCompTimer,
                out var originalTimer) ||
            !memory.TryReadLong(
                activeComponent + (uint)GraphicsLayouts.AnimCompAnimBob,
                out var bob))
            return false;
        if (bob == 0)
        {
            if (memory.TryWriteWord(
                    activeComponent + (uint)GraphicsLayouts.AnimCompTimer,
                    timer))
            {
                return true;
            }

            RestoreWord(
                memory,
                activeComponent + (uint)GraphicsLayouts.AnimCompTimer,
                originalTimer);
            return false;
        }

        if (!TryGetBob(memory, bob, out var vSprite))
            return false;
        var displayX = ((short)x + (short)xTrans) >> 6;
        var displayY = ((short)y + (short)yTrans) >> 6;
        if (!memory.TryReadWord(vSprite + (uint)GraphicsLayouts.VSpriteX, out var originalX) ||
            !memory.TryReadWord(vSprite + (uint)GraphicsLayouts.VSpriteY, out var originalY) ||
            !memory.TryReadWord(bob + (uint)GraphicsLayouts.BobFlags, out var originalFlags))
        {
            return false;
        }

        if (memory.TryWriteWord(
                activeComponent + (uint)GraphicsLayouts.AnimCompTimer,
                timer) &&
            memory.TryWriteWord(
                vSprite + (uint)GraphicsLayouts.VSpriteX,
                unchecked((ushort)displayX)) &&
            memory.TryWriteWord(
                vSprite + (uint)GraphicsLayouts.VSpriteY,
                unchecked((ushort)displayY)) &&
            memory.TryWriteWord(
                bob + (uint)GraphicsLayouts.BobFlags,
                (ushort)(originalFlags | GraphicsLayouts.BobFlagDrawn)))
        {
            return true;
        }

        RestoreWord(
            memory,
            activeComponent + (uint)GraphicsLayouts.AnimCompTimer,
            originalTimer);
        RestoreWord(memory, vSprite + (uint)GraphicsLayouts.VSpriteX, originalX);
        RestoreWord(memory, vSprite + (uint)GraphicsLayouts.VSpriteY, originalY);
        RestoreWord(memory, bob + (uint)GraphicsLayouts.BobFlags, originalFlags);
        return false;
    }

    private static bool TrySwitchSequence(
        IGraphicsMemory memory,
        uint animOb,
        uint component,
        uint rastPort,
        out uint nextComponent)
    {
        nextComponent = component;
        if (!memory.TryReadLong(
                component + (uint)GraphicsLayouts.AnimCompNextSeq,
                out var nextSequence) ||
            nextSequence == 0 ||
            nextSequence == component)
        {
            // A zero/self sequence is a static component.  TimeSet still
            // reloads on the active component, matching the classic timer
            // contract without changing the active Bob.
            return true;
        }

        if (!ProbeRange(memory, nextSequence, GraphicsLayouts.AnimCompSize) ||
            !memory.TryReadLong(
                component + (uint)GraphicsLayouts.AnimCompAnimBob,
                out var oldBob) ||
            !memory.TryReadLong(
                nextSequence + (uint)GraphicsLayouts.AnimCompAnimBob,
                out var newBob) ||
            (newBob != 0 && !TryGetBob(memory, newBob, out _)) ||
            !memory.TryReadLong(
                component + (uint)GraphicsLayouts.AnimCompPrevComp,
                out var previousComponent) ||
            !memory.TryReadLong(
                component + (uint)GraphicsLayouts.AnimCompNextComp,
                out var followingComponent) ||
            (previousComponent != 0 &&
             !ProbeRange(memory, previousComponent, GraphicsLayouts.AnimCompSize)) ||
            (followingComponent != 0 &&
             !ProbeRange(memory, followingComponent, GraphicsLayouts.AnimCompSize)))
        {
            return false;
        }

        var originalLinks = new List<(uint Address, uint Value)>();
        if (!TrySnapshotPointer(
                memory,
                component + (uint)GraphicsLayouts.AnimCompPrevComp,
                originalLinks) ||
            !TrySnapshotPointer(
                memory,
                component + (uint)GraphicsLayouts.AnimCompNextComp,
                originalLinks) ||
            !TrySnapshotPointer(
                memory,
                component + (uint)GraphicsLayouts.AnimCompHeadOb,
                originalLinks) ||
            !TrySnapshotPointer(
                memory,
                nextSequence + (uint)GraphicsLayouts.AnimCompPrevComp,
                originalLinks) ||
            !TrySnapshotPointer(
                memory,
                nextSequence + (uint)GraphicsLayouts.AnimCompNextComp,
                originalLinks) ||
            !TrySnapshotPointer(
                memory,
                nextSequence + (uint)GraphicsLayouts.AnimCompHeadOb,
                originalLinks) ||
            (previousComponent != 0 &&
             !TrySnapshotPointer(
                 memory,
                 previousComponent + (uint)GraphicsLayouts.AnimCompNextComp,
                 originalLinks)) ||
            (followingComponent != 0 &&
             !TrySnapshotPointer(
                 memory,
                 followingComponent + (uint)GraphicsLayouts.AnimCompPrevComp,
                 originalLinks)) ||
            (previousComponent == 0 &&
             !TrySnapshotPointer(
                 memory,
                 animOb + (uint)GraphicsLayouts.AnimObHeadComp,
                 originalLinks)))
        {
            return false;
        }

        var originalOldBob = new List<(uint Address, uint Value)>();
        ushort originalOldBobFlags = 0;
        var oldBobFlagsCaptured = false;
        if (oldBob != 0)
        {
            if (!TryGetBob(memory, oldBob, out var oldVSprite) ||
                !memory.TryReadWord(
                    oldBob + (uint)GraphicsLayouts.BobFlags,
                    out originalOldBobFlags) ||
                !TrySnapshotPointer(
                    memory,
                    oldVSprite + (uint)GraphicsLayouts.VSpriteVSBob,
                    originalOldBob) ||
                !TrySnapshotPointer(
                    memory,
                    oldVSprite + (uint)GraphicsLayouts.VSpritePrev,
                    originalOldBob) ||
                !TrySnapshotPointer(
                    memory,
                    oldVSprite + (uint)GraphicsLayouts.VSpriteNext,
                    originalOldBob))
            {
                return false;
            }

            if (!memory.TryReadLong(
                    oldVSprite + (uint)GraphicsLayouts.VSpritePrev,
                    out var oldPrevious) ||
                !memory.TryReadLong(
                    oldVSprite + (uint)GraphicsLayouts.VSpriteNext,
                    out var oldFollowing) ||
                oldPrevious == 0 || oldFollowing == 0 ||
                !TrySnapshotPointer(
                    memory,
                    oldPrevious + (uint)GraphicsLayouts.VSpriteNext,
                    originalOldBob) ||
                !TrySnapshotPointer(
                    memory,
                    oldFollowing + (uint)GraphicsLayouts.VSpritePrev,
                    originalOldBob))
            {
                return false;
            }

            oldBobFlagsCaptured = true;
        }

        void RollbackSwitch()
        {
            RestorePointers(memory, originalOldBob);
            RestorePointers(memory, originalLinks);
            if (oldBobFlagsCaptured)
            {
                RestoreWord(
                    memory,
                    oldBob + (uint)GraphicsLayouts.BobFlags,
                    originalOldBobFlags);
            }
        }

        // Remove the old active Bob before changing the component chain.  If
        // the list is malformed, the frame remains active and no guest links
        // are partially rewritten.
        if (oldBob != 0 && !RemIBob(memory, oldBob, rastPort, 0, null))
        {
            return false;
        }

        if ((previousComponent != 0 &&
             !memory.TryWriteLong(
                 previousComponent + (uint)GraphicsLayouts.AnimCompNextComp,
                 nextSequence)) ||
            (followingComponent != 0 &&
             !memory.TryWriteLong(
                 followingComponent + (uint)GraphicsLayouts.AnimCompPrevComp,
                 nextSequence)) ||
            !memory.TryWriteLong(
                nextSequence + (uint)GraphicsLayouts.AnimCompPrevComp,
                previousComponent) ||
            !memory.TryWriteLong(
                nextSequence + (uint)GraphicsLayouts.AnimCompNextComp,
                followingComponent) ||
            !memory.TryWriteLong(
                component + (uint)GraphicsLayouts.AnimCompPrevComp,
                0) ||
            !memory.TryWriteLong(
                component + (uint)GraphicsLayouts.AnimCompNextComp,
                0) ||
            !memory.TryWriteLong(
                nextSequence + (uint)GraphicsLayouts.AnimCompHeadOb,
                animOb))
        {
            RollbackSwitch();
            return false;
        }

        if (previousComponent == 0 &&
            !memory.TryWriteLong(
                animOb + (uint)GraphicsLayouts.AnimObHeadComp,
                nextSequence))
        {
            RollbackSwitch();
            return false;
        }

        if (newBob != 0 && !AddBob(memory, newBob, rastPort))
        {
            // Restore the active chain and the old Bob when the replacement
            // cannot be linked.  The public guest list must never expose a
            // half-switched animation frame.
            RollbackSwitch();
            return false;
        }

        nextComponent = nextSequence;
        return true;
    }

    private static bool CollectActiveComponents(
        IGraphicsMemory memory,
        uint animOb,
        out List<uint> components)
    {
        components = new List<uint>();
        if (!memory.TryReadLong(animOb + (uint)GraphicsLayouts.AnimObHeadComp, out var head) || head == 0)
            return true;

        var visited = new HashSet<uint>();
        var current = head;
        for (var count = 0; current != 0 && count < 4096; count++)
        {
            if (!visited.Add(current))
                return false;
            if (!ProbeRange(memory, current, GraphicsLayouts.AnimCompSize))
                return false;
            components.Add(current);
            if (!memory.TryReadLong(
                    current + (uint)GraphicsLayouts.AnimCompNextComp,
                    out current))
                return false;
        }

        return current == 0;
    }

    private static bool CollectAllComponents(
        IGraphicsMemory memory,
        uint animOb,
        out List<uint> components)
    {
        components = new List<uint>();
        if (!CollectActiveComponents(memory, animOb, out var active))
            return false;

        var visited = new HashSet<uint>();
        foreach (var component in active)
        {
            var current = component;
            for (var count = 0; current != 0 && count < 4096; count++)
            {
                if (!visited.Add(current))
                    break;
                if (!ProbeRange(memory, current, GraphicsLayouts.AnimCompSize))
                    return false;
                components.Add(current);
                if (!memory.TryReadLong(
                        current + (uint)GraphicsLayouts.AnimCompNextSeq,
                        out current))
                {
                    return false;
                }
            }

            if (current != 0 && !visited.Contains(current))
                return false;
        }

        return true;
    }

    private static void RollbackAnimOb(
        IGraphicsMemory memory,
        IReadOnlyList<(uint Address, uint Value)> originalPublication,
        IReadOnlyList<(uint Address, uint Value)> originalComponents,
        List<uint> added)
    {
        foreach (var bob in added)
        {
            if (TryGetBob(memory, bob, out var vSprite))
                GraphicsGelsOperations.RemVSprite(memory, vSprite);
        }
        RestorePointers(memory, originalComponents);
        RestorePointers(memory, originalPublication);
    }

    private static bool TryGetBob(IGraphicsMemory memory, uint bob, out uint vSprite)
    {
        vSprite = 0;
        return bob != 0 && ProbeRange(memory, bob, GraphicsLayouts.BobSize) &&
               memory.TryReadLong(bob + (uint)GraphicsLayouts.BobVSprite, out vSprite) &&
               vSprite != 0 && ProbeRange(memory, vSprite, GraphicsLayouts.VSpriteSize);
    }

    private static bool TryCollectBobRasterRegions(
        IGraphicsMemory memory,
        uint rastPort,
        out List<BobRasterRegion> regions)
    {
        regions = new List<BobRasterRegion>();
        if (!TryReadGelsList(
                memory,
                rastPort,
                out var sprites,
                out _,
                out _,
                out _,
                out _,
                out _))
        {
            return false;
        }

        var bobAddresses = new List<uint>();
        var seen = new HashSet<uint>();
        foreach (var sprite in sprites)
        {
            if (!memory.TryReadLong(
                    sprite.Address + (uint)GraphicsLayouts.VSpriteVSBob,
                    out var bob))
            {
                return false;
            }

            if (bob == 0 || !seen.Add(bob))
                continue;

            if (!TryGetBob(memory, bob, out var vSprite) ||
                !memory.TryReadWord(
                    vSprite + (uint)GraphicsLayouts.VSpriteFlags,
                    out var vSpriteFlags) ||
                !memory.TryReadWord(bob + (uint)GraphicsLayouts.BobFlags, out _))
            {
                return false;
            }

            if ((vSpriteFlags &
                 (GraphicsLayouts.VSpriteSaveBack | GraphicsLayouts.VSpriteBackSaved)) == 0)
                continue;

            if (!memory.TryReadLong(
                    bob + (uint)GraphicsLayouts.BobSaveBuffer,
                    out var saveBuffer) ||
                saveBuffer == 0)
            {
                return false;
            }

            bobAddresses.Add(bob);
        }

        if (bobAddresses.Count == 0)
            return true;

        if (!GraphicsRasterOperations.TryReadBitmap(memory, rastPort, out var bitmap))
            return false;

        foreach (var bob in bobAddresses)
        {
            if (!TryCreateBobRasterRegion(memory, bitmap, rastPort, bob, out var region))
                return false;

            regions.Add(region);
        }

        return true;
    }

    private static bool TryCreateBobRasterRegion(
        IGraphicsMemory memory,
        GraphicsRasterOperations.BitmapInfo bitmap,
        uint rastPort,
        uint bob,
        out BobRasterRegion region)
    {
        region = default;
        if (!TryGetBob(memory, bob, out var vSprite) ||
            !memory.TryReadWord(bob + (uint)GraphicsLayouts.BobFlags, out var flags) ||
            !memory.TryReadWord(
                vSprite + (uint)GraphicsLayouts.VSpriteFlags,
                out var vSpriteFlags) ||
            !memory.TryReadLong(bob + (uint)GraphicsLayouts.BobSaveBuffer, out var saveBuffer) ||
            !memory.TryReadWord(vSprite + (uint)GraphicsLayouts.VSpriteX, out var rawX) ||
            !memory.TryReadWord(vSprite + (uint)GraphicsLayouts.VSpriteY, out var rawY) ||
            !memory.TryReadWord(vSprite + (uint)GraphicsLayouts.VSpriteOldX, out var rawOldX) ||
            !memory.TryReadWord(vSprite + (uint)GraphicsLayouts.VSpriteOldY, out var rawOldY) ||
            !memory.TryReadWord(vSprite + (uint)GraphicsLayouts.VSpriteWidth, out var widthWords) ||
            !memory.TryReadWord(vSprite + (uint)GraphicsLayouts.VSpriteHeight, out var height) ||
            !memory.TryReadWord(vSprite + (uint)GraphicsLayouts.VSpriteDepth, out var depth) ||
            saveBuffer == 0 || widthWords == 0 || height == 0 || depth == 0)
        {
            return false;
        }

        var savedDepth = Math.Min((int)depth, bitmap.Depth);
        if (savedDepth == 0)
            return false;

        var x = unchecked((short)rawX);
        var y = unchecked((short)rawY);
        var oldX = unchecked((short)rawOldX);
        var oldY = unchecked((short)rawOldY);
        if ((vSpriteFlags & GraphicsLayouts.VSpriteSaveBack) != 0 &&
            !TryProbeBobRasterRegion(
                memory,
                bitmap,
                saveBuffer,
                x,
                y,
                widthWords,
                height,
                savedDepth))
        {
            return false;
        }

        if ((vSpriteFlags & GraphicsLayouts.VSpriteBackSaved) != 0 &&
            !TryProbeBobRasterRegion(
                memory,
                bitmap,
                saveBuffer,
                oldX,
                oldY,
                widthWords,
                height,
                savedDepth))
        {
            return false;
        }

        region = new BobRasterRegion(
            bob,
            vSprite,
            saveBuffer,
            flags,
            vSpriteFlags,
            x,
            y,
            oldX,
            oldY,
            widthWords,
            height,
            savedDepth,
            bitmap,
            rastPort);
        return true;
    }

    private static bool TryProbeBobRasterRegion(
        IGraphicsMemory memory,
        GraphicsRasterOperations.BitmapInfo bitmap,
        uint saveBuffer,
        short x,
        short y,
        ushort widthWords,
        ushort height,
        int depth)
    {
        if (x < 0 || y < 0 || (x & 15) != 0)
            return false;

        var wordX = x >> 4;
        if ((long)wordX + widthWords > bitmap.BytesPerRow / 2 ||
            (long)y + height > bitmap.Rows)
        {
            return false;
        }

        var wordCount = (ulong)widthWords * height * (uint)depth;
        if (wordCount == 0 || wordCount > int.MaxValue / 2u ||
            !ProbeRange(memory, saveBuffer, checked((int)(wordCount * 2u))))
        {
            return false;
        }

        for (var plane = 0; plane < depth; plane++)
        {
            var planePointerAddress = (ulong)bitmap.Address +
                (uint)GraphicsLayouts.BitMapPlanes + (uint)(plane * 4);
            if (planePointerAddress > uint.MaxValue ||
                !memory.TryReadLong((uint)planePointerAddress, out var planeAddress) ||
                planeAddress == 0)
            {
                return false;
            }

            for (var row = 0; row < height; row++)
            {
                for (var word = 0; word < widthWords; word++)
                {
                    var sourceAddress = (ulong)planeAddress +
                        ((ulong)((int)y + row) * (uint)bitmap.BytesPerRow) +
                        (uint)((wordX + word) * 2);
                    if (sourceAddress > uint.MaxValue ||
                        !memory.TryReadWord((uint)sourceAddress, out _))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private static bool TrySaveBobBackground(
        IGraphicsMemory memory,
        GraphicsRasterOperations.BitmapInfo bitmap,
        BobRasterRegion region)
    {
        var wordIndex = 0u;
        var wordX = region.X >> 4;
        for (var plane = 0; plane < region.Depth; plane++)
        {
            var planePointerAddress = (ulong)bitmap.Address +
                (uint)GraphicsLayouts.BitMapPlanes + (uint)(plane * 4);
            if (planePointerAddress > uint.MaxValue ||
                !memory.TryReadLong((uint)planePointerAddress, out var planeAddress) ||
                planeAddress == 0)
            {
                return false;
            }

            for (var row = 0; row < region.Height; row++)
            {
                for (var word = 0; word < region.WidthWords; word++)
                {
                    var sourceAddress = (ulong)planeAddress +
                        ((ulong)((int)region.Y + row) * (uint)bitmap.BytesPerRow) +
                        (uint)((wordX + word) * 2);
                    if (sourceAddress > uint.MaxValue ||
                        !memory.TryReadWord((uint)sourceAddress, out var value) ||
                        !memory.TryWriteWord(
                            region.SaveBuffer + wordIndex * 2u,
                            value))
                    {
                        return false;
                    }

                    wordIndex++;
                }
            }
        }

        return memory.TryWriteWord(
                   region.VSprite + (uint)GraphicsLayouts.VSpriteOldX,
                   unchecked((ushort)region.X)) &&
               memory.TryWriteWord(
                   region.VSprite + (uint)GraphicsLayouts.VSpriteOldY,
                   unchecked((ushort)region.Y)) &&
               memory.TryWriteWord(
                   region.VSprite + (uint)GraphicsLayouts.VSpriteFlags,
                   (ushort)(region.VSpriteFlags | GraphicsLayouts.VSpriteBackSaved)) &&
               memory.TryWriteWord(
                   region.Bob + (uint)GraphicsLayouts.BobFlags,
                   (ushort)((region.Flags | GraphicsLayouts.BobFlagDrawn) &
                            ~GraphicsLayouts.BobFlagNix));
    }

    private static bool TryRestoreBobBackground(
        IGraphicsMemory memory,
        BobRasterRegion region)
    {
        var bitmap = region.Bitmap;
        var wordX = region.OldX >> 4;
        var wordIndex = 0u;
        for (var plane = 0; plane < region.Depth; plane++)
        {
            var planePointerAddress = (ulong)bitmap.Address +
                (uint)GraphicsLayouts.BitMapPlanes + (uint)(plane * 4);
            if (planePointerAddress > uint.MaxValue ||
                !memory.TryReadLong((uint)planePointerAddress, out var planeAddress) ||
                planeAddress == 0)
            {
                return false;
            }

            for (var row = 0; row < region.Height; row++)
            {
                for (var word = 0; word < region.WidthWords; word++)
                {
                    var destinationAddress = (ulong)planeAddress +
                        ((ulong)((int)region.OldY + row) * (uint)bitmap.BytesPerRow) +
                        (uint)((wordX + word) * 2);
                    if (destinationAddress > uint.MaxValue ||
                        !memory.TryReadWord((uint)destinationAddress, out _) ||
                        !memory.TryReadWord(
                            region.SaveBuffer + wordIndex * 2u,
                            out _))
                    {
                        return false;
                    }

                    wordIndex++;
                }
            }
        }

        wordIndex = 0;
        for (var plane = 0; plane < region.Depth; plane++)
        {
            var planePointerAddress = (ulong)bitmap.Address +
                (uint)GraphicsLayouts.BitMapPlanes + (uint)(plane * 4);
            if (!memory.TryReadLong((uint)planePointerAddress, out var planeAddress) ||
                planeAddress == 0)
            {
                return false;
            }

            for (var row = 0; row < region.Height; row++)
            {
                for (var word = 0; word < region.WidthWords; word++)
                {
                    var destinationAddress = (ulong)planeAddress +
                        ((ulong)((int)region.OldY + row) * (uint)bitmap.BytesPerRow) +
                        (uint)((wordX + word) * 2);
                    if (destinationAddress > uint.MaxValue ||
                        !memory.TryReadWord(region.SaveBuffer + wordIndex * 2u, out var value) ||
                        !memory.TryWriteWord((uint)destinationAddress, value))
                    {
                        return false;
                    }

                    wordIndex++;
                }
            }
        }

        return memory.TryWriteWord(
                   region.VSprite + (uint)GraphicsLayouts.VSpriteFlags,
                   (ushort)(region.VSpriteFlags & ~GraphicsLayouts.VSpriteBackSaved)) &&
               memory.TryWriteWord(
                   region.Bob + (uint)GraphicsLayouts.BobFlags,
                   (ushort)(region.Flags & ~GraphicsLayouts.BobFlagDrawn));
    }

    private static bool TrySaveBobBackground(
        IGraphicsMemory memory,
        BobRasterRegion region)
        => TrySaveBobBackground(memory, region.Bitmap, region);

    private static bool TryRestoreBobBackground(
        IGraphicsMemory memory,
        uint bob,
        uint rastPort)
    {
        if (!TryGetBob(memory, bob, out var vSprite) ||
            !memory.TryReadWord(
                vSprite + (uint)GraphicsLayouts.VSpriteFlags,
                out var vSpriteFlags) ||
            (vSpriteFlags & GraphicsLayouts.VSpriteBackSaved) == 0)
        {
            return true;
        }

        if (!GraphicsRasterOperations.TryReadBitmap(memory, rastPort, out var bitmap) ||
            !TryCreateBobRasterRegion(memory, bitmap, rastPort, bob, out var region))
        {
            return false;
        }

        return TryRestoreBobBackground(memory, region);
    }

    private static bool ValidateList(IGraphicsMemory memory, uint rastPort)
    {
        return TryReadGelsList(
            memory,
            rastPort,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _);
    }

    private static bool TryReadGelsList(
        IGraphicsMemory memory,
        uint rastPort,
        out List<CollisionSprite> list,
        out uint collisionHandler,
        out short leftmost,
        out short rightmost,
        out short topmost,
        out short bottommost)
    {
        list = new List<CollisionSprite>();
        collisionHandler = 0;
        leftmost = rightmost = topmost = bottommost = 0;
        if (!ProbeRange(memory, rastPort, GraphicsLayouts.RastPortMinimumSize) ||
            !memory.TryReadLong(rastPort + (uint)GraphicsLayouts.RastPortGelsInfo, out var gelsInfo) ||
            gelsInfo == 0 || !ProbeRange(memory, gelsInfo, GraphicsLayouts.GelsInfoSize) ||
            !memory.TryReadLong(gelsInfo + (uint)GraphicsLayouts.GelsInfoHead, out var head) ||
            !memory.TryReadLong(gelsInfo + (uint)GraphicsLayouts.GelsInfoTail, out var tail) ||
            head == 0 || tail == 0 || head == tail ||
            !memory.TryReadLong(
                gelsInfo + (uint)GraphicsLayouts.GelsInfoCollisionHandler,
                out collisionHandler) ||
            !TryReadSignedWord(memory, gelsInfo + (uint)GraphicsLayouts.GelsInfoLeftmost, out leftmost) ||
            !TryReadSignedWord(memory, gelsInfo + (uint)GraphicsLayouts.GelsInfoRightmost, out rightmost) ||
            !TryReadSignedWord(memory, gelsInfo + (uint)GraphicsLayouts.GelsInfoTopmost, out topmost) ||
            !TryReadSignedWord(memory, gelsInfo + (uint)GraphicsLayouts.GelsInfoBottommost, out bottommost) ||
            // The classic API permits a collision table sized only through
            // the highest callback the caller supplies.  Entry zero is the
            // boundary callback, while higher entries are read on demand by
            // TryReadCollisionRoutine below; requiring all sixteen entries
            // would reject valid compact tables.
            (collisionHandler != 0 && !ProbeRange(memory, collisionHandler, 4)))
        {
            return false;
        }

        if (!ProbeRange(memory, head, GraphicsLayouts.VSpriteSize) ||
            !ProbeRange(memory, tail, GraphicsLayouts.VSpriteSize) ||
            !memory.TryReadLong(
                head + (uint)GraphicsLayouts.VSpritePrev,
                out var headPrevious) ||
            headPrevious != 0)
            return false;

        var visited = new HashSet<uint> { head };
        var current = head;
        for (var count = 0; count < 4096; count++)
        {
            if (!memory.TryReadLong(current + (uint)GraphicsLayouts.VSpriteNext, out var next) ||
                next == 0 || !visited.Add(next) ||
                !ProbeRange(memory, next, GraphicsLayouts.VSpriteSize) ||
                !memory.TryReadLong(next + (uint)GraphicsLayouts.VSpritePrev, out var previous) ||
                previous != current)
            {
                return false;
            }

            if (next == tail)
            {
                return memory.TryReadLong(
                           tail + (uint)GraphicsLayouts.VSpriteNext,
                           out var tailNext) &&
                       tailNext == 0;
            }

            if (!memory.TryReadWord(next + (uint)GraphicsLayouts.VSpriteX, out var x) ||
                !memory.TryReadWord(next + (uint)GraphicsLayouts.VSpriteY, out var y) ||
                !memory.TryReadWord(next + (uint)GraphicsLayouts.VSpriteWidth, out var width) ||
                !memory.TryReadWord(next + (uint)GraphicsLayouts.VSpriteHeight, out var height) ||
                !memory.TryReadWord(next + (uint)GraphicsLayouts.VSpriteMeMask, out var meMask) ||
                !memory.TryReadWord(next + (uint)GraphicsLayouts.VSpriteHitMask, out var hitMask) ||
                !memory.TryReadLong(next + (uint)GraphicsLayouts.VSpriteCollMask, out var collMask))
            {
                return false;
            }

            list.Add(new CollisionSprite(
                next,
                unchecked((short)x),
                unchecked((short)y),
                width,
                height,
                meMask,
                hitMask,
                collMask));
            current = next;
        }

        return false;
    }

    private static bool TryReadCollisionRoutine(
        IGraphicsMemory memory,
        uint collisionHandler,
        int index,
        out uint routine)
    {
        routine = 0;
        if (collisionHandler == 0 || index < 0 || index >= GraphicsLayouts.CollisionTableEntries)
            return false;
        return memory.TryReadLong(collisionHandler + (uint)(index * 4), out routine) &&
               routine != 0;
    }

    private static bool MasksOverlap(
        IGraphicsMemory memory,
        CollisionSprite first,
        CollisionSprite second)
    {
        if (first.CollMask == 0 || second.CollMask == 0 ||
            first.Width == 0 || second.Width == 0 ||
            first.Height == 0 || second.Height == 0)
            return false;

        var firstRight = (long)first.X + ((long)first.Width * 16) - 1;
        var secondRight = (long)second.X + ((long)second.Width * 16) - 1;
        var firstBottom = (long)first.Y + first.Height - 1;
        var secondBottom = (long)second.Y + second.Height - 1;
        var left = Math.Max(first.X, second.X);
        var right = Math.Min(firstRight, secondRight);
        var top = Math.Max(first.Y, second.Y);
        var bottom = Math.Min(firstBottom, secondBottom);
        if (left > right || top > bottom)
            return false;

        var area = (ulong)(right - left + 1) * (ulong)(bottom - top + 1);
        if (area > 8_000_000)
            return false;

        for (var y = top; y <= bottom; y++)
        {
            var firstRow = (int)(y - first.Y);
            var secondRow = (int)(y - second.Y);
            for (var x = left; x <= right; x++)
            {
                var firstColumn = (int)(x - first.X);
                var secondColumn = (int)(x - second.X);
                if (MaskBit(memory, first, firstRow, firstColumn) &&
                    MaskBit(memory, second, secondRow, secondColumn))
                    return true;
            }
        }

        return false;
    }

    private static bool MaskBit(
        IGraphicsMemory memory,
        CollisionSprite sprite,
        int row,
        int column)
    {
        var word = ((ulong)row * sprite.Width) + (uint)(column >> 4);
        var address = (ulong)sprite.CollMask + (word * 2ul);
        return address <= uint.MaxValue &&
               memory.TryReadWord((uint)address, out var value) &&
               (value & (0x8000 >> (column & 15))) != 0;
    }

    private readonly record struct CollisionSprite(
        uint Address,
        short X,
        short Y,
        ushort Width,
        ushort Height,
        ushort MeMask,
        ushort HitMask,
        uint CollMask);

    private readonly record struct BobRasterRegion(
        uint Bob,
        uint VSprite,
        uint SaveBuffer,
        ushort Flags,
        ushort VSpriteFlags,
        short X,
        short Y,
        short OldX,
        short OldY,
        ushort WidthWords,
        ushort Height,
        int Depth,
        GraphicsRasterOperations.BitmapInfo Bitmap,
        uint RastPort);

    private static bool TryReadSignedWord(
        IGraphicsMemory memory,
        uint address,
        out short value)
    {
        if (!memory.TryReadWord(address, out var raw))
        {
            value = 0;
            return false;
        }
        value = unchecked((short)raw);
        return true;
    }

    private static bool Allocate(
        IGraphicsAllocatorBackend allocator,
        BufferSet set,
        uint bytes,
        GraphicsMemoryClass memoryClass,
        out uint address)
    {
        address = 0;
        if (bytes == 0)
            return false;

        var allocationSucceeded = allocator.TryAllocate(bytes, memoryClass, out address);
        if (!allocationSucceeded || address == 0 || (address & 1u) != 0)
        {
            // A provider may return a provisional address with a false
            // status.  The allocation is still owned by this operation until
            // it is either recorded in BufferSet or explicitly released.
            if (address != 0)
                allocator.Free(address, bytes, memoryClass);
            return false;
        }

        set.Allocations.Add(new BufferAllocation(address, bytes, memoryClass));
        return true;
    }

    private static bool TrySnapshotPointer(
        IGraphicsMemory memory,
        uint address,
        List<(uint Address, uint Value)> pointers)
    {
        if (!memory.TryReadLong(address, out var value))
            return false;
        pointers.Add((address, value));
        return true;
    }

    private static void RestorePointers(
        IGraphicsMemory memory,
        IReadOnlyList<(uint Address, uint Value)> pointers)
    {
        foreach (var pointer in pointers)
            RestoreLong(memory, pointer.Address, pointer.Value);
    }

    private static void RestoreWord(
        IGraphicsMemory memory,
        uint address,
        ushort value)
    {
        _ = memory.TryWriteByte(address, (byte)(value >> 8));
        _ = memory.TryWriteByte(address + 1u, (byte)value);
    }

    private static void RestoreLong(
        IGraphicsMemory memory,
        uint address,
        uint value)
    {
        _ = memory.TryWriteByte(address, (byte)(value >> 24));
        _ = memory.TryWriteByte(address + 1u, (byte)(value >> 16));
        _ = memory.TryWriteByte(address + 2u, (byte)(value >> 8));
        _ = memory.TryWriteByte(address + 3u, (byte)value);
    }

    private static void FreeSet(IGraphicsAllocatorBackend allocator, BufferSet set)
    {
        for (var index = set.Allocations.Count - 1; index >= 0; index--)
        {
            var allocation = set.Allocations[index];
            allocator.Free(allocation.Address, allocation.Bytes, allocation.MemoryClass);
        }
        set.Allocations.Clear();
    }

    private static bool TrySize(ulong value, out uint bytes)
    {
        bytes = 0;
        if (value == 0 || value > uint.MaxValue)
            return false;
        bytes = (uint)value;
        return true;
    }

    private static bool TrySnapshotOwnedPointer(
        IGraphicsMemory memory,
        uint address,
        uint expected,
        List<(uint Address, uint Value)> pointers)
    {
        if (!memory.TryReadLong(address, out var actual))
            return false;
        if (expected != 0 && actual == expected)
            pointers.Add((address, actual));
        return true;
    }

    private static bool ClearRange(IGraphicsMemory memory, uint address, uint bytes)
    {
        for (var offset = 0u; offset < bytes; offset++)
        {
            if (!memory.TryWriteByte(address + offset, 0))
                return false;
        }
        return true;
    }

    private static bool TryReadWord(IGraphicsMemory memory, uint address, out ushort value)
        => memory.TryReadWord(address, out value);

    private static bool TryReadLong(IGraphicsMemory memory, uint address, out uint value)
        => memory.TryReadLong(address, out value);

    private static bool ProbeRange(IGraphicsMemory memory, uint address, int bytes)
    {
        if (address == 0 || (address & 1u) != 0 || bytes <= 0 ||
            address > uint.MaxValue - (uint)(bytes - 1))
            return false;
        for (var offset = 0; offset < bytes; offset++)
        {
            if (!memory.TryReadByte(address + (uint)offset, out _))
                return false;
        }
        return true;
    }
}
