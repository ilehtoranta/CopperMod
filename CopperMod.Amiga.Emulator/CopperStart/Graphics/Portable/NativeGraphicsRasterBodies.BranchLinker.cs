using System;
using System.Collections.Generic;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>
/// Relocation-safe branch-linking support for generated native graphics bodies.
/// Kept separate from vector emitters so generated fragments can move without
/// weakening the 68000 signed-word branch audit.
/// </summary>
internal static partial class NativeGraphicsRasterBodies
{
    // One stream-wide linker owns all fixed-width, relocatable relays. Callers
    // reserve islands at construction-time fragment boundaries, then chain
    // the short BRA.W hops when a conditional/BSR edge cannot reach its final
    // label.
    private sealed class NativeGraphicsBranchIslandAllocator
    {
        private readonly List<byte> code;
        private readonly HashSet<int> unlinkedExtensions = new();

        internal NativeGraphicsBranchIslandAllocator(List<byte> code)
        {
            this.code = code;
        }

        internal int ReserveAtFragmentBoundary(string name, out int extensionOffset)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A native graphics fragment needs a name.", nameof(name));
            if ((code.Count & 1) != 0)
                throw new InvalidOperationException(
                    "Native graphics fragment boundaries must remain word aligned.");
            currentBranchRangeAudit?.RecordFragmentBoundary(name, code.Count);
            var offset = AppendUnpatchedBranchIsland(code, out extensionOffset);
            unlinkedExtensions.Add(extensionOffset);
            return offset;
        }

        internal void Link(int extensionOffset, int targetOffset)
        {
            if (!unlinkedExtensions.Contains(extensionOffset))
                throw new InvalidOperationException("Native graphics relay was linked more than once.");
            EnsureSignedWordReachable(extensionOffset, targetOffset);
            unlinkedExtensions.Remove(extensionOffset);
            PatchBranch(code, extensionOffset, targetOffset);
        }

        internal void LinkChain(
            int sourceExtensionOffset,
            int targetOffset,
            params int[] relayExtensionOffsets)
        {
            var currentExtensionOffset = sourceExtensionOffset;
            foreach (var relayExtensionOffset in relayExtensionOffsets)
            {
                EnsureSignedWordReachable(currentExtensionOffset, relayExtensionOffset - 2);
                currentExtensionOffset = relayExtensionOffset;
            }

            EnsureSignedWordReachable(currentExtensionOffset, targetOffset);

            var reservations = new HashSet<int> { sourceExtensionOffset };
            if (!unlinkedExtensions.Contains(sourceExtensionOffset))
                throw new InvalidOperationException("Native graphics relay chain source was linked more than once.");
            foreach (var relayExtensionOffset in relayExtensionOffsets)
            {
                if (!reservations.Add(relayExtensionOffset))
                    throw new InvalidOperationException("Native graphics relay chain contains a duplicate island.");
                if (!unlinkedExtensions.Contains(relayExtensionOffset))
                    throw new InvalidOperationException("Native graphics relay chain contains an already-linked island.");
            }

            currentBranchRangeAudit?.RecordChain();
            if (!unlinkedExtensions.Remove(sourceExtensionOffset))
                throw new InvalidOperationException("Native graphics relay chain source was linked more than once.");

            currentExtensionOffset = sourceExtensionOffset;
            foreach (var relayExtensionOffset in relayExtensionOffsets)
            {
                if (!unlinkedExtensions.Remove(relayExtensionOffset))
                    throw new InvalidOperationException("Native graphics relay chain contains an already-linked island.");

                PatchBranch(code, currentExtensionOffset, relayExtensionOffset - 2);
                currentExtensionOffset = relayExtensionOffset;
            }

            PatchBranch(code, currentExtensionOffset, targetOffset);
        }

        internal void Complete()
        {
            if (unlinkedExtensions.Count != 0)
                throw new InvalidOperationException("Native graphics relay was left unlinked.");
        }

        private static void EnsureSignedWordReachable(int extensionOffset, int targetOffset)
        {
            var displacement = checked(targetOffset - extensionOffset);
            if (displacement < short.MinValue || displacement > short.MaxValue)
                throw new InvalidOperationException(
                    $"Native graphics relay hop is out of range (extension {extensionOffset}, target {targetOffset}, displacement {displacement}).");
        }
    }

    private static int AppendUnpatchedBranchIsland(List<byte> code, out int extensionOffset)
    {
        var offset = code.Count;
        Append(code, BranchAlwaysWord);
        extensionOffset = code.Count;
        Append(code, 0);
        currentBranchRangeAudit?.RecordRelay();
        return offset;
    }

    private static void PatchBranch(List<byte> code, int extensionOffset, int targetOffset)
    {
        // Copper68k's DBcc decoder uses the extension-word address as its
        // branch base, matching the conditional-branch helper used by these
        // generated bodies.
        var resolvedTargetOffset = targetOffset;
        var displacement = checked(resolvedTargetOffset - extensionOffset);
        if ((displacement < short.MinValue || displacement > short.MaxValue) &&
            targetOffset == 0)
        {
            // The shared native fallback is a single RTS. Once the generated
            // stream grows past the 68000 Bcc.W reach, duplicate that same
            // terminal body at the current construction frontier and branch
            // to the local copy. This keeps the code HUNK-relocatable: no
            // absolute JMP.L relocation is introduced merely to reach RTS.
            var localFallbackOffset = code.Count;
            Append(code, ReturnFromSubroutine);
            currentBranchRangeAudit?.RecordLocalFallback(localFallbackOffset);
            resolvedTargetOffset = localFallbackOffset;
            displacement = checked(resolvedTargetOffset - extensionOffset);
        }

        if (displacement < short.MinValue || displacement > short.MaxValue)
            throw new InvalidOperationException(
                $"Native graphics branch is out of range (extension {extensionOffset}, target {targetOffset}, displacement {displacement}).");

        code[extensionOffset] = (byte)((ushort)displacement >> 8);
        code[extensionOffset + 1] = (byte)displacement;
        currentBranchRangeAudit?.Record(extensionOffset, resolvedTargetOffset, displacement);
    }

    private static void PatchBranchPreferShort(List<byte> code, int extensionOffset, int targetOffset)
    {
        var displacement = checked(targetOffset - extensionOffset);
        if (displacement is >= sbyte.MinValue and <= sbyte.MaxValue and not 0)
        {
            var opcodeOffset = extensionOffset - 2;
            var opcode = (ushort)((code[opcodeOffset] << 8) | code[opcodeOffset + 1]);
            if ((opcode & 0xF000) == 0x6000)
            {
                // Preserve the four-byte construction footprint: the opcode
                // becomes Bcc.S and the old extension slot is a fall-through
                // NOP. Branch audit offsets remain extension-relative.
                code[opcodeOffset + 1] = unchecked((byte)(sbyte)displacement);
                code[extensionOffset] = 0x4E;
                code[extensionOffset + 1] = 0x71;
                currentBranchRangeAudit?.Record(extensionOffset, targetOffset, displacement);
                return;
            }
        }

        PatchBranch(code, extensionOffset, targetOffset);
    }
}
