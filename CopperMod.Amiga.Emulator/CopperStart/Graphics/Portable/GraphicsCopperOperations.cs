using System;
using System.Collections.Generic;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>
/// Guest-visible construction of intermediate user copper lists.
///
/// UCopperListInit is deliberately limited to the allocation/publication
/// slice here.  The list registry owns only the internal CopList and CopIns
/// buffers it allocated; hardware copper-list merging remains an explicit
/// CopperStart/native boundary.
/// </summary>
internal static class GraphicsCopperOperations
{
    internal const int MergeOk = 0;
    internal const int MergeNoMemory = 1;
    internal const int MergeNoOp = 2;

    private const ushort MaxInstructionCount = ushort.MaxValue;

    internal sealed class Registry
    {
        internal Dictionary<uint, Allocation> Lists { get; } = new();
    }

    internal sealed class Allocation
    {
        internal required uint UserList { get; init; }
        internal required ushort MaxCount { get; init; }
        internal List<BlockAllocation> Blocks { get; } = new();
    }

    internal sealed class BlockAllocation
    {
        internal required uint CopList { get; init; }
        internal required uint Instructions { get; init; }
        internal required uint InstructionBytes { get; init; }
        internal byte[] OriginalCopList { get; set; } = Array.Empty<byte>();
        internal byte[] OriginalInstructions { get; set; } = Array.Empty<byte>();
    }

    /// <summary>
    /// Implements the Kickstart UCopperListInit/CINIT contract for a caller-
    /// supplied UCopList.  The returned address is the FirstCopList
    /// substructure, not the UCopList itself.
    /// </summary>
    internal static uint UCopperListInit(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend allocator,
        Registry registry,
        uint userList,
        ushort instructionCount)
    {
        if (!IsEvenAddress(userList) ||
            instructionCount == 0 ||
            instructionCount > MaxInstructionCount ||
            registry.Lists.ContainsKey(userList) ||
            !TryProbeRange(memory, userList, (uint)GraphicsLayouts.UCopListSize))
        {
            return 0;
        }

        if (!TrySnapshot(
                memory,
                userList,
                GraphicsLayouts.UCopListSize,
                out var originalUser))
        {
            return 0;
        }

        if (!TryAllocateBlock(
                memory,
                allocator,
                instructionCount,
                out var block) ||
            !TryClear(memory, userList, (uint)GraphicsLayouts.UCopListSize) ||
            !TryWriteLongAt(
                memory,
                userList,
                GraphicsLayouts.UCopListFirstCopList,
                block.CopList) ||
            !TryWriteLongAt(
                memory,
                userList,
                GraphicsLayouts.UCopListCopList,
                block.CopList))
        {
            Restore(memory, userList, originalUser);
            DiscardBlock(memory, allocator, block);
            return 0;
        }

        var allocation = new Allocation
        {
            UserList = userList,
            MaxCount = instructionCount
        };
        allocation.Blocks.Add(block);
        CommitBlock(block);
        registry.Lists.Add(userList, allocation);
        return block.CopList;
    }

    /// <summary>
    /// Appends the pseudo MOVE instruction at the current CopPtr.  Like the
    /// Kickstart CMove vector, this does not bump the pointer; the CMOVE macro
    /// performs the subsequent CBump call.
    /// </summary>
    internal static bool CMove(
        IGraphicsMemory memory,
        Registry registry,
        uint userList,
        uint hardwareRegister,
        ushort value)
    {
        if (!TryGetCurrentInstruction(
                memory,
                registry,
                userList,
                out _,
                out var instruction))
        {
            return false;
        }

        if (!TrySnapshot(memory, instruction, GraphicsLayouts.CopInsSize, out var original))
            return false;

        // Copper register addresses are even words in the low 9-bit custom
        // register space.  Masking also accepts the absolute $DFFxxx form
        // used by custom-register pointers in the classic C macros.
        if (TryWriteWordAt(
                memory,
                instruction,
                GraphicsLayouts.CopInsOpCode,
                0) &&
            TryWriteWordAt(
                memory,
                instruction,
                GraphicsLayouts.CopInsArg0,
                (ushort)(hardwareRegister & 0x01FEu)) &&
            TryWriteWordAt(
                memory,
                instruction,
                GraphicsLayouts.CopInsArg1,
                value))
        {
            return true;
        }

        Restore(memory, instruction, original);
        return false;
    }

    /// <summary>
    /// Appends the pseudo WAIT instruction at the current CopPtr.  Pointer
    /// advancement is deliberately left to CBump, matching CWAIT's macro
    /// expansion and the public CWait vector contract.
    /// </summary>
    internal static bool CWait(
        IGraphicsMemory memory,
        Registry registry,
        uint userList,
        ushort vertical,
        ushort horizontal)
    {
        if (!TryGetCurrentInstruction(
                memory,
                registry,
                userList,
                out _,
                out var instruction))
        {
            return false;
        }

        if (!TrySnapshot(memory, instruction, GraphicsLayouts.CopInsSize, out var original))
            return false;

        if (TryWriteWordAt(
                memory,
                instruction,
                GraphicsLayouts.CopInsOpCode,
                1) &&
            TryWriteWordAt(
                memory,
                instruction,
                GraphicsLayouts.CopInsArg0,
                vertical) &&
            TryWriteWordAt(
                memory,
                instruction,
                GraphicsLayouts.CopInsArg1,
                horizontal))
        {
            return true;
        }

        Restore(memory, instruction, original);
        return false;
    }

    /// <summary>
    /// Returns the validated current instruction address for the native
    /// overlay admission boundary. CMove and CWait publish only this
    /// CopIns record; the caller-owned UCopList links remain input state
    /// until the separate CBump vector advances them.
    /// </summary>
    internal static bool TryGetCurrentInstructionAddress(
        IGraphicsMemory memory,
        Registry registry,
        uint userList,
        out uint instruction)
    {
        return TryGetCurrentInstruction(
            memory,
            registry,
            userList,
            out _,
            out instruction);
    }

    /// <summary>
    /// Returns the exact guest publication branch selected by CBump. A
    /// non-full block updates CopList.CopPtr and CopList.Count. A full block
    /// either links a newly allocated block through CopList.Next and the
    /// caller's UCopList, or republishes only the caller's current CopList
    /// link when the next block already exists.
    /// </summary>
    internal static bool TryGetCBumpPublicationState(
        IGraphicsMemory memory,
        Registry registry,
        uint userList,
        out uint copList,
        out ushort count,
        out ushort maxCount,
        out uint nextCopList)
    {
        copList = 0;
        count = 0;
        maxCount = 0;
        nextCopList = 0;
        if (!TryGetCurrentBlock(
                memory,
                registry,
                userList,
                out var allocation,
                out var block,
                out count,
                out maxCount) ||
            !TryReadLongAt(
                memory,
                block.CopList,
                GraphicsLayouts.CopListNext,
                out nextCopList))
        {
            return false;
        }

        if (nextCopList != 0 && FindBlock(allocation, nextCopList) is null)
            return false;

        copList = block.CopList;
        return true;
    }

    /// <summary>Advances the current intermediate instruction pointer.</summary>
    internal static bool CBump(
        IGraphicsMemory memory,
        Registry registry,
        IGraphicsAllocatorBackend allocator,
        uint userList)
    {
        if (!TryGetCurrentBlock(
                memory,
                registry,
                userList,
                out var allocation,
                out var block,
                out var count,
                out var maxCount))
        {
            return false;
        }

        if (count < maxCount)
        {
            if (!TryReadLongAt(
                    memory,
                    block.CopList,
                    GraphicsLayouts.CopListCopPtr,
                    out var instruction) ||
                !TryAdd(instruction, (uint)GraphicsLayouts.CopInsSize, out var next))
            {
                return false;
            }

            if (!TrySnapshot(
                    memory,
                    block.CopList,
                    GraphicsLayouts.CopListSize,
                    out var original))
            {
                return false;
            }

            if (TryWriteLongAt(
                    memory,
                    block.CopList,
                    GraphicsLayouts.CopListCopPtr,
                    next) &&
                TryWriteWordAt(
                    memory,
                    block.CopList,
                    GraphicsLayouts.CopListCount,
                    (ushort)(count + 1)))
            {
                return true;
            }

            Restore(memory, block.CopList, original);
            return false;
        }

        // Once a block is full, the classic CBump contract advances to a new
        // intermediate block.  Allocate lazily so CINIT's count remains a
        // per-block capacity and unused lists do not consume extra memory.
        if (!TryReadLongAt(
                memory,
                block.CopList,
                GraphicsLayouts.CopListNext,
                out var nextCopList))
        {
            return false;
        }

        if (nextCopList == 0)
        {
            if (!TryAllocateBlock(memory, allocator, allocation.MaxCount, out var nextBlock))
                return false;

            byte[] originalCurrent = Array.Empty<byte>();
            byte[] originalUser = Array.Empty<byte>();
            if (!TrySnapshot(
                    memory,
                    block.CopList,
                    GraphicsLayouts.CopListSize,
                    out originalCurrent) ||
                !TrySnapshot(
                    memory,
                    userList,
                    GraphicsLayouts.UCopListSize,
                    out originalUser) ||
                !TryWriteLongAt(
                    memory,
                    block.CopList,
                    GraphicsLayouts.CopListNext,
                    nextBlock.CopList) ||
                !TryWriteLongAt(
                    memory,
                    userList,
                    GraphicsLayouts.UCopListCopList,
                    nextBlock.CopList))
            {
                // Both guest links are part of one publication.  Restore
                // them if either long-word write was only partially applied.
                if (originalCurrent.Length != 0)
                    Restore(memory, block.CopList, originalCurrent);
                if (originalUser.Length != 0)
                    Restore(memory, userList, originalUser);
                DiscardBlock(memory, allocator, nextBlock);
                return false;
            }

            allocation.Blocks.Add(nextBlock);
            CommitBlock(nextBlock);
            return true;
        }

        var existingBlock = FindBlock(allocation, nextCopList);
        if (existingBlock is null ||
            !TrySnapshot(
                memory,
                userList,
                GraphicsLayouts.UCopListSize,
                out var originalExistingUser))
        {
            return false;
        }

        if (TryWriteLongAt(
                memory,
                userList,
                GraphicsLayouts.UCopListCopList,
                existingBlock.CopList))
        {
            return true;
        }

        Restore(memory, userList, originalExistingUser);
        return false;
    }

    /// <summary>
    /// Implements the CEND macro's documented CWAIT(10000,255) followed by
    /// CBump sequence.  CEND is a macro rather than a public LVO.
    /// </summary>
    internal static bool CEnd(
        IGraphicsMemory memory,
        Registry registry,
        IGraphicsAllocatorBackend allocator,
        uint userList)
        => CWait(memory, registry, userList, 10000, 255) &&
           CBump(memory, registry, allocator, userList);

    /// <summary>
    /// Releases only an intermediate CopList returned by this registry's
    /// UCopperListInit call.  The caller-owned UCopList remains allocated by
    /// its owner, but its two internal links are cleared before the buffers
    /// are returned to the public allocator.
    /// </summary>
    internal static bool FreeCopList(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend allocator,
        Registry registry,
        uint copList)
    {
        // The native FreeCopList loop is empty for a NULL head. Claim that
        // idempotent void form without requiring a registry entry; non-null
        // foreign or stale lists remain strict and available to the provider.
        if (copList == 0)
            return true;

        uint userList = 0;
        Allocation? allocation = null;
        foreach (var pair in registry.Lists)
        {
            if (FindBlock(pair.Value, copList) is not null)
            {
                userList = pair.Key;
                allocation = pair.Value;
                break;
            }
        }

        if (allocation is null ||
            !TryProbeRange(memory, userList, (uint)GraphicsLayouts.UCopListSize) ||
            !TrySnapshot(
                memory,
                userList,
                GraphicsLayouts.UCopListSize,
                out var original))
        {
            return false;
        }

        if (!TryWriteLongAt(
                memory,
                userList,
                GraphicsLayouts.UCopListFirstCopList,
                0) ||
            !TryWriteLongAt(
                memory,
                userList,
                GraphicsLayouts.UCopListCopList,
                0))
        {
            Restore(memory, userList, original);
            return false;
        }

        registry.Lists.Remove(userList);
        foreach (var block in allocation.Blocks)
            FreeBlock(allocator, block);
        return true;
    }

    /// <summary>
    /// Resolves the caller-owned <c>UCopList</c> envelope that
    /// <see cref="FreeCopList"/> will clear for a registry-owned intermediate
    /// list.  Native-overlay admission uses this before entering the
    /// transactional teardown so a read-only provider mapping cannot make the
    /// portable path attempt a partial two-link publication.
    /// </summary>
    internal static bool TryGetUserListForCopList(
        Registry registry,
        uint copList,
        out uint userList)
    {
        userList = 0;
        if (copList == 0)
            return true;

        foreach (var pair in registry.Lists)
        {
            if (FindBlock(pair.Value, copList) is not null)
            {
                userList = pair.Key;
                return true;
            }
        }

        return false;
    }

    private static bool TryAllocateBlock(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend allocator,
        ushort instructionCount,
        out BlockAllocation block)
    {
        block = null!;
        var instructionBytes = (uint)GraphicsLayouts.CopInsSize * instructionCount;
        uint copList = 0;
        uint instructions = 0;
        var copListAllocated = allocator.TryAllocate(
            (uint)GraphicsLayouts.CopListSize,
            GraphicsMemoryClass.Public,
            out copList);
        if (!copListAllocated || !IsEvenAddress(copList))
        {
            if (copList != 0)
                allocator.Free(
                    copList,
                    (uint)GraphicsLayouts.CopListSize,
                    GraphicsMemoryClass.Public);
            return false;
        }

        var instructionsAllocated = allocator.TryAllocate(
            instructionBytes,
            GraphicsMemoryClass.Public,
            out instructions);
        if (!instructionsAllocated || !IsEvenAddress(instructions))
        {
            if (instructions != 0)
                allocator.Free(
                    instructions,
                    instructionBytes,
                    GraphicsMemoryClass.Public);
            allocator.Free(
                copList,
                (uint)GraphicsLayouts.CopListSize,
                GraphicsMemoryClass.Public);
            return false;
        }

        byte[] originalCopList = Array.Empty<byte>();
        byte[] originalInstructions = Array.Empty<byte>();
        var envelopesCaptured =
            instructionBytes <= int.MaxValue &&
            TrySnapshot(
                memory,
                copList,
                GraphicsLayouts.CopListSize,
                out originalCopList) &&
            TrySnapshot(
                memory,
                instructions,
                (int)instructionBytes,
                out originalInstructions);
        if (!envelopesCaptured ||
            !TryClear(memory, copList, (uint)GraphicsLayouts.CopListSize) ||
            !TryClear(memory, instructions, instructionBytes) ||
            !TryInitializeCopList(memory, copList, instructions, instructionCount))
        {
            // Internal buffers are still guest-visible public allocations.
            // Restore both complete spans before returning them so a recycled
            // CopList cannot retain a partial pointer or initialization word.
            Restore(memory, instructions, originalInstructions);
            Restore(memory, copList, originalCopList);
            allocator.Free(
                instructions,
                instructionBytes,
                GraphicsMemoryClass.Public);
            allocator.Free(
                copList,
                (uint)GraphicsLayouts.CopListSize,
                GraphicsMemoryClass.Public);
            return false;
        }

        block = new BlockAllocation
        {
            CopList = copList,
            Instructions = instructions,
            InstructionBytes = instructionBytes,
            OriginalCopList = originalCopList,
            OriginalInstructions = originalInstructions
        };
        return true;
    }

    private static void DiscardBlock(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend allocator,
        BlockAllocation? block)
    {
        if (block is null)
            return;

        Restore(memory, block.Instructions, block.OriginalInstructions);
        Restore(memory, block.CopList, block.OriginalCopList);
        FreeBlock(allocator, block);
    }

    private static void CommitBlock(BlockAllocation block)
    {
        // Once the block is linked into the registry, its bytes are owned by
        // the live copper list rather than a failed-publication journal.
        block.OriginalCopList = Array.Empty<byte>();
        block.OriginalInstructions = Array.Empty<byte>();
    }

    private static void FreeBlock(
        IGraphicsAllocatorBackend allocator,
        BlockAllocation? block)
    {
        if (block is null)
            return;

        allocator.Free(
            block.Instructions,
            block.InstructionBytes,
            GraphicsMemoryClass.Public);
        allocator.Free(
            block.CopList,
            (uint)GraphicsLayouts.CopListSize,
            GraphicsMemoryClass.Public);
    }

    private static BlockAllocation? FindBlock(
        Allocation allocation,
        uint copList)
    {
        foreach (var block in allocation.Blocks)
        {
            if (block.CopList == copList)
                return block;
        }

        return null;
    }

    private static bool TryInitializeCopList(
        IGraphicsMemory memory,
        uint copList,
        uint instructions,
        ushort instructionCount)
        => TryWriteLongAt(memory, copList, GraphicsLayouts.CopListCopIns, instructions) &&
           TryWriteLongAt(memory, copList, GraphicsLayouts.CopListCopPtr, instructions) &&
           TryWriteWordAt(memory, copList, GraphicsLayouts.CopListCount, 0) &&
           TryWriteWordAt(memory, copList, GraphicsLayouts.CopListMaxCount, instructionCount);

    private static bool TryGetCurrentInstruction(
        IGraphicsMemory memory,
        Registry registry,
        uint userList,
        out uint copList,
        out uint instruction)
        => TryGetCurrentInstruction(
            memory,
            registry,
            userList,
            out copList,
            out instruction,
            out _,
            out _);

    private static bool TryGetCurrentInstruction(
        IGraphicsMemory memory,
        Registry registry,
        uint userList,
        out uint copList,
        out uint instruction,
        out ushort count,
        out ushort maxCount)
    {
        copList = 0;
        instruction = 0;
        count = 0;
        maxCount = 0;
        if (!TryGetCurrentBlock(
                memory,
                registry,
                userList,
                out _,
                out var block,
                out count,
                out maxCount) ||
            count >= maxCount ||
            !TryReadLongAt(
                memory,
                block.CopList,
                GraphicsLayouts.CopListCopPtr,
                out instruction))
        {
            return false;
        }

        copList = block.CopList;
        if (block.Instructions > uint.MaxValue - block.InstructionBytes ||
            instruction < block.Instructions ||
            instruction >= block.Instructions + block.InstructionBytes)
        {
            return false;
        }

        var offset = instruction - block.Instructions;
        return offset % (uint)GraphicsLayouts.CopInsSize == 0 &&
            offset == (uint)count * (uint)GraphicsLayouts.CopInsSize;
    }

    private static bool TryGetCurrentBlock(
        IGraphicsMemory memory,
        Registry registry,
        uint userList,
        out Allocation allocation,
        out BlockAllocation block,
        out ushort count,
        out ushort maxCount)
    {
        allocation = null!;
        block = null!;
        count = 0;
        maxCount = 0;
        if (!registry.Lists.TryGetValue(userList, out var foundAllocation))
        {
            return false;
        }

        if (!TryReadLongAt(
                memory,
                userList,
                GraphicsLayouts.UCopListCopList,
                out var copList))
        {
            return false;
        }

        var foundBlock = FindBlock(foundAllocation, copList);
        if (foundBlock is null)
            return false;

        if (!TryReadLongAt(
                memory,
                foundBlock.CopList,
                GraphicsLayouts.CopListCopIns,
                out var firstInstruction) ||
            firstInstruction != foundBlock.Instructions ||
            !TryReadLongAt(
                memory,
                foundBlock.CopList,
                GraphicsLayouts.CopListCopPtr,
                out var instruction) ||
            !TryReadWordAt(
                memory,
                foundBlock.CopList,
                GraphicsLayouts.CopListCount,
                out count) ||
            !TryReadWordAt(
                memory,
                foundBlock.CopList,
                GraphicsLayouts.CopListMaxCount,
                out maxCount) ||
            maxCount != foundAllocation.MaxCount ||
            foundBlock.Instructions > uint.MaxValue - foundBlock.InstructionBytes ||
            instruction < foundBlock.Instructions ||
            instruction > foundBlock.Instructions + foundBlock.InstructionBytes ||
            instruction - foundBlock.Instructions !=
                (uint)count * (uint)GraphicsLayouts.CopInsSize)
        {
            return false;
        }

        allocation = foundAllocation;
        block = foundBlock;
        return count <= maxCount;
    }

    private static bool TryProbeRange(
        IGraphicsMemory memory,
        uint address,
        uint byteCount)
    {
        if (!IsEvenAddress(address))
            return false;

        for (var offset = 0u; offset < byteCount; offset++)
        {
            if (!TryAdd(address, offset, out var current) ||
                !memory.TryReadByte(current, out _))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsEvenAddress(uint address)
        => address != 0 && (address & 1u) == 0;

    private static bool TryClear(
        IGraphicsMemory memory,
        uint address,
        uint byteCount)
    {
        for (var offset = 0u; offset < byteCount; offset++)
        {
            if (!TryAdd(address, offset, out var current) ||
                !memory.TryWriteByte(current, 0))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TrySnapshot(
        IGraphicsMemory memory,
        uint address,
        int byteCount,
        out byte[] original)
    {
        original = Array.Empty<byte>();
        if (byteCount <= 0)
            return false;

        original = new byte[byteCount];
        for (var offset = 0; offset < byteCount; offset++)
        {
            if (!TryAdd(address, (uint)offset, out var current) ||
                !memory.TryReadByte(current, out original[offset]))
            {
                original = Array.Empty<byte>();
                return false;
            }
        }

        return true;
    }

    private static void Restore(
        IGraphicsMemory memory,
        uint address,
        byte[] original)
    {
        for (var offset = original.Length - 1; offset >= 0; offset--)
        {
            if (TryAdd(address, (uint)offset, out var current))
                _ = memory.TryWriteByte(current, original[offset]);
        }
    }

    private static bool TryWriteLongAt(
        IGraphicsMemory memory,
        uint address,
        int offset,
        uint value)
        => TryAdd(address, (uint)offset, out var target) &&
           memory.TryWriteLong(target, value);

    private static bool TryReadLongAt(
        IGraphicsMemory memory,
        uint address,
        int offset,
        out uint value)
    {
        if (!TryAdd(address, (uint)offset, out var target))
        {
            value = 0;
            return false;
        }

        return memory.TryReadLong(target, out value);
    }

    private static bool TryWriteWordAt(
        IGraphicsMemory memory,
        uint address,
        int offset,
        ushort value)
        => TryAdd(address, (uint)offset, out var target) &&
           memory.TryWriteWord(target, value);

    private static bool TryReadWordAt(
        IGraphicsMemory memory,
        uint address,
        int offset,
        out ushort value)
    {
        if (!TryAdd(address, (uint)offset, out var target))
        {
            value = 0;
            return false;
        }

        return memory.TryReadWord(target, out value);
    }

    private static bool TryAdd(uint address, uint offset, out uint result)
    {
        if (address > uint.MaxValue - offset)
        {
            result = 0;
            return false;
        }

        result = address + offset;
        return true;
    }
}
