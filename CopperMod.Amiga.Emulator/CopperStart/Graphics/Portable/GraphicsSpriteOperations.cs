using System;
using System.Collections.Generic;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>
/// Portable simple-sprite ownership and guest state.  The standard OCS/ECS
/// position/control envelope is maintained in guest memory when the caller
/// supplied a valid PosCtlData pointer; display-DMA publication remains an
/// explicit host/native boundary.
/// </summary>
internal static class GraphicsSpriteOperations
{
    internal sealed class Registry
    {
        internal bool[] Allocated { get; } = new bool[8];

        private readonly Dictionary<uint, ExtendedAllocation> _extended = new();

        internal bool TryRegisterExtended(
            uint extSprite,
            uint image,
            uint imageBytes,
            GraphicsMemoryClass imageClass)
        {
            if (extSprite == 0 || _extended.ContainsKey(extSprite))
                return false;

            _extended.Add(extSprite, new ExtendedAllocation(image, imageBytes, imageClass));
            return true;
        }

        internal bool TryTakeExtended(uint extSprite, out ExtendedAllocation allocation)
            => _extended.Remove(extSprite, out allocation);

        internal bool OwnsExtended(uint extSprite)
            => _extended.ContainsKey(extSprite);
    }

    internal readonly record struct ExtendedAllocation(
        uint Image,
        uint ImageBytes,
        GraphicsMemoryClass ImageClass);

    private const int MaximumTagItems = 512;
    private const uint TagDone = 0;
    private const uint TagIgnore = 1;
    private const uint TagMore = 2;
    private const uint TagSkip = 3;
    private const int DefaultSpriteWidth = 16;
    private const int MaximumHardwareSpriteWidth = 64;
    private const int SpriteWordPixels = 16;
    private const int MaximumReplication = 2;
    private const ushort DefaultUnassignedSprite = ushort.MaxValue;
    // SPRxPOS/SPRxCTL use low-resolution, non-interlaced View coordinates.
    // ViewPort Dx/Dy offsets are added before the optional hires/interlace
    // conversion in TryWritePositionControl.
    private const int SpriteViewOriginX = 0x81;
    private const int SpriteViewOriginY = 0x2C;

    internal static int GetSprite(
        IGraphicsMemory memory,
        Registry registry,
        uint sprite,
        short requested)
    {
        if (sprite == 0 || !ProbeSprite(memory, sprite))
            return GraphicsRasterOperations.Failure;

        var selected = -1;
        if (requested == -1)
        {
            for (var index = 0; index < registry.Allocated.Length; index++)
            {
                if (!registry.Allocated[index])
                {
                    selected = index;
                    break;
                }
            }
        }
        else if (requested >= 0 && requested < registry.Allocated.Length &&
                 !registry.Allocated[requested])
        {
            selected = requested;
        }

        if (selected < 0)
        {
            // Kickstart records failure in the public num field as well as
            // returning -1, which lets callers inspect a failed request
            // without relying on the return register.
            _ = TryPublishUnassignedSprites(memory, sprite, 0);
            return GraphicsRasterOperations.Failure;
        }

        if (!memory.TryWriteWord(
                sprite + (uint)GraphicsLayouts.SimpleSpriteNum,
                (ushort)selected))
        {
            return GraphicsRasterOperations.Failure;
        }

        registry.Allocated[selected] = true;
        return selected;
    }

    internal static bool IsValidSimpleSprite(
        IGraphicsMemory memory,
        uint sprite)
        => sprite != 0 &&
           ProbeSprite(memory, sprite) &&
           memory.TryReadWord(
               sprite + (uint)GraphicsLayouts.SimpleSpriteNum,
               out var originalNum) &&
           memory.TryWriteWord(
               sprite + (uint)GraphicsLayouts.SimpleSpriteNum,
               originalNum);

    internal static bool FreeSprite(Registry registry, short sprite)
    {
        if (sprite < 0 || sprite >= registry.Allocated.Length)
            return false;

        registry.Allocated[sprite] = false;
        return true;
    }

    internal static bool IsAllocated(Registry registry, short sprite)
        => sprite >= 0 &&
           sprite < registry.Allocated.Length &&
           registry.Allocated[sprite];

    /// <summary>
    /// Updates the guest-visible SimpleSprite position and, when PosCtlData is
    /// present, regenerates the two OCS/ECS hardware control words.  Viewport
    /// coordinates are converted to the low-resolution, non-interlaced sprite
    /// coordinate space used by SPRxPOS/SPRxCTL.
    /// </summary>
    internal static bool MoveSprite(
        IGraphicsMemory memory,
        uint viewPort,
        uint sprite,
        short x,
        short y,
        IGraphicsSpriteBackend? backend = null)
    {
        if (!ValidateViewport(memory, viewPort) ||
            !ProbeSprite(memory, sprite) ||
            !TryPreparePositionControl(
                memory,
                viewPort,
                sprite,
                x,
                y,
                out var positionControl))
        {
            return false;
        }

        byte[] originalPositionControl = Array.Empty<byte>();
        if (!TrySnapshot(
                memory,
                sprite,
                GraphicsLayouts.SimpleSpriteSize,
                out var originalSprite) ||
            (positionControl.PosCtlData != 0 &&
             !TrySnapshot(
                 memory,
                 positionControl.PosCtlData,
                 GraphicsLayouts.SpriteImagePosCtlSize,
                 out originalPositionControl)))
        {
            return false;
        }

        if (!memory.TryWriteWord(
                sprite + (uint)GraphicsLayouts.SimpleSpriteX,
                unchecked((ushort)x)) ||
            !memory.TryWriteWord(
                sprite + (uint)GraphicsLayouts.SimpleSpriteY,
                unchecked((ushort)y)) ||
            (positionControl.PosCtlData != 0 &&
             (!memory.TryWriteWord(
                  positionControl.PosCtlData,
                  positionControl.Position) ||
              !memory.TryWriteWord(
                  positionControl.PosCtlData + 2u,
                  positionControl.Control))))
        {
            Restore(memory, sprite, originalSprite);
            if (positionControl.PosCtlData != 0)
                Restore(memory, positionControl.PosCtlData, originalPositionControl);
            return false;
        }

        backend?.MoveSprite(viewPort, sprite, x, y);
        return true;
    }

    private static bool TryPreparePositionControl(
        IGraphicsMemory memory,
        uint viewPort,
        uint sprite,
        short x,
        short y,
        out PositionControl positionControl)
    {
        positionControl = default;
        if (!memory.TryReadLong(
                sprite + (uint)GraphicsLayouts.SimpleSpritePosCtlData,
                out var posCtlData) ||
            posCtlData == 0)
        {
            // A zero PosCtlData is common while a SimpleSprite envelope is
            // being assembled. Preserve x/y and let the optional display
            // backend own eventual publication.
            return true;
        }

        if (!ProbeRange(memory, posCtlData, GraphicsLayouts.SpriteImagePosCtlSize) ||
            !memory.TryReadWord(
                sprite + (uint)GraphicsLayouts.SimpleSpriteHeight,
                out var height) ||
            !memory.TryReadWord(posCtlData + 2u, out var oldControl))
        {
            return false;
        }

        var spriteX = (int)x;
        var spriteY = (int)y;
        if (viewPort != 0)
        {
            if (!memory.TryReadWord(
                    viewPort + (uint)GraphicsLayouts.ViewPortDxOffset,
                    out var dxWord) ||
                !memory.TryReadWord(
                    viewPort + (uint)GraphicsLayouts.ViewPortDyOffset,
                    out var dyWord) ||
                !memory.TryReadWord(
                    viewPort + (uint)GraphicsLayouts.ViewPortModes,
                    out var modes))
            {
                return false;
            }

            spriteX += unchecked((short)dxWord);
            spriteY += unchecked((short)dyWord);
            if ((modes & GraphicsModeIds.HiresMode) != 0)
                spriteX >>= 1;
            if ((modes & GraphicsModeIds.InterlaceMode) != 0)
                spriteY >>= 1;
        }

        var hStart = SpriteViewOriginX + spriteX;
        var vStart = SpriteViewOriginY + spriteY;
        var vStop = vStart + height;
        var position = unchecked((ushort)((vStart << 8) | ((hStart >> 1) & 0xFF)));
        var control = unchecked((ushort)(
            (vStop << 8) |
            (hStart & 1) |
            ((vStop & 0x100) != 0 ? 0x0002 : 0) |
            ((vStart & 0x100) != 0 ? 0x0004 : 0) |
            (oldControl & GraphicsLayouts.SpriteAttachedFlag)));

        positionControl = new PositionControl(posCtlData, position, control);
        return true;
    }

    private readonly record struct PositionControl(
        uint PosCtlData,
        ushort Position,
        ushort Control);

    /// <summary>
    /// Rebinds a SimpleSprite to a caller-provided sprite-image stream.  The
    /// height field is supplied by the caller before ChangeSprite; validating
    /// the corresponding pos/ctl, image, and reserved-word span prevents a
    /// malformed CHIP-memory pointer from crossing the portable boundary.
    /// </summary>
    internal static bool ChangeSprite(
        IGraphicsMemory memory,
        uint viewPort,
        uint sprite,
        uint newData,
        IGraphicsSpriteBackend? backend = null)
    {
        if (!ValidateViewport(memory, viewPort) ||
            !ProbeSprite(memory, sprite) ||
            newData == 0 ||
            (newData & 1u) != 0 ||
            !memory.TryReadWord(
                sprite + (uint)GraphicsLayouts.SimpleSpriteHeight,
                out var height))
        {
            return false;
        }

        var byteCount = 8ul + ((ulong)height * 4ul);
        if (byteCount > uint.MaxValue ||
            memory is IGraphicsDisplayMemory displayMemory &&
            !displayMemory.IsDisplayDmaRange(newData, (uint)byteCount) ||
            !ProbeRange(memory, newData, (uint)byteCount) ||
            !memory.TryReadLong(
                sprite + (uint)GraphicsLayouts.SimpleSpritePosCtlData,
                out var originalData))
        {
            return false;
        }

        if (!memory.TryWriteLong(
                sprite + (uint)GraphicsLayouts.SimpleSpritePosCtlData,
                newData))
        {
            RestoreLong(
                memory,
                sprite + (uint)GraphicsLayouts.SimpleSpritePosCtlData,
                originalData);
            return false;
        }

        backend?.ChangeSprite(viewPort, sprite, newData);
        return true;
    }

    /// <summary>
    /// Allocates the V39 standard 16-pixel sprite-image envelope from a
    /// standard planar guest bitmap.  The native OCS/ECS form supports the
    /// documented -2..2 resolution replication factors; wider sprite forms
    /// remain an explicit boundary rather than being silently truncated.
    /// </summary>
    internal static uint AllocSpriteDataA(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend allocator,
        Registry registry,
        uint bitMap,
        uint tags)
    {
        if (!TryReadTags(memory, tags, out var tagValues) ||
            !TryReadSpriteOptions(tagValues, out var options) ||
            !IsValidReplication(options.XReplication) ||
            !IsValidReplication(options.YReplication))
        {
            return 0;
        }

        SourceBitmap source = default;
        ushort wordWidth;
        uint outputHeight;
        if (options.OldDataFormat)
        {
            // SPRITEA_OldDataFormat consumes the legacy two-word-per-row
            // sprite image that starts four bytes into the supplied address.
            // The old representation carries no height, so the documented
            // OutputHeight tag is required; it is also a two-plane, 16-pixel
            // form and cannot express attached or wide hardware sprites.
            if (options.Attached ||
                (bitMap & 1u) != 0 ||
                options.Width is 0 or > SpriteWordPixels ||
                options.OutputHeight == 0 ||
                options.OutputHeight > ushort.MaxValue ||
                !ProbeRange(
                    memory,
                    bitMap,
                    checked(4u + (options.OutputHeight * 4u))))
            {
                return 0;
            }

            wordWidth = 1;
            outputHeight = options.OutputHeight;
        }
        else
        {
            if (!TryGetSpriteWordWidth(options.Width, out wordWidth) ||
                !TryReadSourceBitmap(memory, bitMap, out source) ||
                source.Depth < (options.Attached ? 4 : 2))
            {
                return 0;
            }

            outputHeight = options.OutputHeight == 0 ? source.Rows : options.OutputHeight;
            if (outputHeight == 0 ||
                outputHeight > source.Rows ||
                outputHeight > ushort.MaxValue)
            {
                return 0;
            }
        }

        var imageBytesWide = 8ul + ((ulong)outputHeight * 4ul * wordWidth);
        if (imageBytesWide > uint.MaxValue)
            return 0;

        var imageBytes = (uint)imageBytesWide;
        var wordsBuilt = options.OldDataFormat
            ? TryBuildOldFormatSpriteWords(memory, bitMap, options, outputHeight, out var words)
            : TryBuildSpriteWords(memory, source, options, wordWidth, outputHeight, out words);
        if (!wordsBuilt)
            return 0;

        var extensionAllocationSucceeded = allocator.TryAllocate(
            (uint)GraphicsLayouts.ExtSpriteSize,
            GraphicsMemoryClass.Public,
            out var extSprite);
        if (!extensionAllocationSucceeded || !IsEvenAddress(extSprite))
        {
            // A false allocator status normally comes with a zero out
            // address, but a provider may return a provisional envelope.
            // Nothing has been published yet, so release that public span
            // before declining the sprite conversion.
            if (extSprite != 0)
            {
                allocator.Free(
                    extSprite,
                    (uint)GraphicsLayouts.ExtSpriteSize,
                    GraphicsMemoryClass.Public);
            }

            return 0;
        }

        var imageAllocationSucceeded = allocator.TryAllocate(
            imageBytes,
            GraphicsMemoryClass.Chip,
            out var image);
        byte[] originalExtSprite = Array.Empty<byte>();
        byte[] originalImage = Array.Empty<byte>();
        var imageStorageValid = imageAllocationSucceeded &&
                                IsEvenAddress(image) &&
                                (memory is not IGraphicsDisplayMemory displayMemory ||
                                 displayMemory.IsDisplayDmaRange(image, imageBytes));
        var envelopesCaptured = imageStorageValid &&
                                imageBytes <= int.MaxValue &&
                                TrySnapshot(
                                    memory,
                                    extSprite,
                                    GraphicsLayouts.ExtSpriteSize,
                                    out originalExtSprite) &&
                                TrySnapshot(
                                    memory,
                                    image,
                                    (int)imageBytes,
                                    out originalImage);
        var published = envelopesCaptured &&
                        TryClearRange(memory, extSprite, GraphicsLayouts.ExtSpriteSize) &&
                        TryClearRange(memory, image, imageBytes) &&
                        WriteSpriteData(memory, extSprite, image, outputHeight, wordWidth, options, words) &&
                        registry.TryRegisterExtended(extSprite, image, imageBytes, GraphicsMemoryClass.Chip);
        if (!published)
        {
            // Both envelopes are caller-visible guest storage.  Restore the
            // complete pre-call bytes before releasing either allocation so a
            // recycled span cannot expose a half-published sprite or poison a
            // later retry after a fault in the final write/registry step.
            Restore(memory, image, originalImage);
            Restore(memory, extSprite, originalExtSprite);
            if (image != 0)
                allocator.Free(image, imageBytes, GraphicsMemoryClass.Chip);

            allocator.Free(extSprite, (uint)GraphicsLayouts.ExtSpriteSize, GraphicsMemoryClass.Public);
            return 0;
        }

        return extSprite;
    }

    /// <summary>Allocates a hardware sprite number for an ExtSprite envelope.</summary>
    internal static int GetExtSpriteA(
        IGraphicsMemory memory,
        Registry registry,
        uint extSprite,
        uint tags)
    {
        if (!TryReadGetExtSpriteRequest(
                memory,
                extSprite,
                tags,
                out var requested,
                out var attachedSprite,
                out var softSprite,
                out var scanDoubled) ||
            softSprite ||
            scanDoubled)
        {
            return GraphicsRasterOperations.Failure;
        }

        var selected = attachedSprite == 0
            ? ReserveSprite(registry, requested)
            : ReserveSpritePair(registry, requested, out _);
        if (selected < 0)
        {
            // Keep the extended manager consistent with GetSprite for an
            // automatic allocation: a valid "next free" request records the
            // unassigned sentinel in the public SimpleSprite prefix as well
            // as returning -1.  An explicit-number or already-reserved pair
            // retains its guest numbers, matching the existing transactional
            // attached-pair contract.  The request preflight above still
            // leaves malformed/foreign envelopes untouched for
            // native/provider ownership; this publication is only reached
            // after the guest spans and tags are structurally valid.
            if (requested == -1)
            {
                _ = TryPublishUnassignedSprites(memory, extSprite, attachedSprite);
            }
            return GraphicsRasterOperations.Failure;
        }

        if (attachedSprite == 0)
        {
            if (!memory.TryWriteWord(
                    extSprite + (uint)GraphicsLayouts.SimpleSpriteNum,
                    (ushort)selected))
            {
                registry.Allocated[selected] = false;
                return GraphicsRasterOperations.Failure;
            }

            return selected;
        }

        if (!TryPublishAttachedPair(
                memory,
                extSprite,
                attachedSprite,
                (ushort)selected,
                (ushort)(selected + 1)))
        {
            registry.Allocated[selected] = false;
            registry.Allocated[selected + 1] = false;
            return GraphicsRasterOperations.Failure;
        }

        return selected;
    }

    /// <summary>
    /// Gives a display/provider backend first-class ownership of the
    /// GSTAG_SOFTSPRITE and GSTAG_SCANDOUBLED forms while retaining the
    /// portable implementation for ordinary hardware sprites.  A false
    /// return means the provider declined the request; callers may then leave
    /// a native Kickstart vector untouched.
    /// </summary>
    internal static bool TryGetExtSpriteA(
        IGraphicsMemory memory,
        Registry registry,
        uint extSprite,
        uint tags,
        IGraphicsSpriteBackend? backend,
        out int result)
    {
        result = GraphicsRasterOperations.Failure;
        if (!TryReadGetExtSpriteRequest(
                memory,
                extSprite,
                tags,
                out var requested,
                out var attachedSprite,
                out var softSprite,
                out var scanDoubled))
        {
            return false;
        }

        if (softSprite || scanDoubled)
        {
            return backend?.TryGetExtSprite(
                       extSprite,
                       tags,
                       requested,
                       attachedSprite,
                       softSprite,
                       scanDoubled,
                       out result) == true;
        }

        result = GetExtSpriteA(memory, registry, extSprite, tags);
        return true;
    }

    internal static bool IsValidGetExtSpriteRequest(
        IGraphicsMemory memory,
        uint extSprite,
        uint tags)
    {
        if (!TryReadGetExtSpriteRequest(
                memory,
                extSprite,
                tags,
                out _,
                out var attachedSprite,
                out _,
                out _ ) ||
            !memory.TryReadWord(
                extSprite + (uint)GraphicsLayouts.SimpleSpriteNum,
                out var originalNum))
        {
            return false;
        }

        // The vector publishes SimpleSprite.num.  A same-value write is a
        // non-destructive writable-envelope probe for the native overlay.
        if (!memory.TryWriteWord(
            extSprite + (uint)GraphicsLayouts.SimpleSpriteNum,
            originalNum))
        {
            return false;
        }

        if (attachedSprite == 0)
            return true;

        if (!TryProbeWritableWord(
                memory,
                attachedSprite + (uint)GraphicsLayouts.SimpleSpriteNum) ||
            !TryProbeWritableWord(
                memory,
                attachedSprite + (uint)GraphicsLayouts.ExtSpriteFlags) ||
            !memory.TryReadLong(
                attachedSprite + (uint)GraphicsLayouts.SimpleSpritePosCtlData,
                out var posCtlData))
        {
            return false;
        }

        return posCtlData == 0 ||
               posCtlData <= uint.MaxValue - 2u &&
               TryProbeWritableWord(memory, posCtlData + 2u);
    }

    /// <summary>
    /// Replaces the guest-visible ExtSprite state after validating the new
    /// sprite-image envelope.  Hardware copper/DMA publication remains an
    /// explicit display backend concern, just as for SimpleSprite.
    /// </summary>
    internal static bool ChangeExtSpriteA(
        IGraphicsMemory memory,
        uint viewPort,
        uint oldSprite,
        uint newSprite,
        uint tags)
    {
        if (!ValidateViewport(memory, viewPort) ||
            !ProbeExtSprite(memory, oldSprite) ||
            !ProbeExtSprite(memory, newSprite) ||
            !TryReadTags(memory, tags, out var tagValues) ||
            !TryReadExtSpriteOptions(tagValues, out _, out var attachedSprite, out var softSprite, out var scanDoubled) ||
            attachedSprite != 0 ||
            softSprite ||
            scanDoubled ||
            !TryReadExtSprite(memory, newSprite, out var source) ||
            !IsSupportedWordWidth(source.WordWidth) ||
            !ProbeSpriteImage(memory, source.PosCtlData, source.Height, source.WordWidth))
        {
            return false;
        }

        if (!TryReadExtSprite(memory, oldSprite, out _))
            return false;

        if (!TrySnapshot(
                memory,
                oldSprite,
                GraphicsLayouts.ExtSpriteSize,
                out var original))
        {
            return false;
        }

        if (memory.TryWriteLong(
                oldSprite + (uint)GraphicsLayouts.SimpleSpritePosCtlData,
                source.PosCtlData) &&
            memory.TryWriteWord(
                oldSprite + (uint)GraphicsLayouts.SimpleSpriteHeight,
                source.Height) &&
            memory.TryWriteWord(
                oldSprite + (uint)GraphicsLayouts.SimpleSpriteX,
                source.X) &&
            memory.TryWriteWord(
                oldSprite + (uint)GraphicsLayouts.SimpleSpriteY,
                source.Y) &&
            memory.TryWriteWord(
                oldSprite + (uint)GraphicsLayouts.SimpleSpriteNum,
                source.Num) &&
            memory.TryWriteWord(
                oldSprite + (uint)GraphicsLayouts.ExtSpriteWordWidth,
                source.WordWidth) &&
            memory.TryWriteWord(
                oldSprite + (uint)GraphicsLayouts.ExtSpriteFlags,
                source.Flags))
        {
            return true;
        }

        Restore(memory, oldSprite, original);
        return false;
    }

    /// <summary>
    /// Provider-aware ChangeExtSpriteA entry.  Standard hardware forms use
    /// the same transactional portable update as ChangeExtSpriteA; only
    /// software/scan-doubled requests cross the explicit backend boundary.
    /// </summary>
    internal static bool TryChangeExtSpriteA(
        IGraphicsMemory memory,
        uint viewPort,
        uint oldSprite,
        uint newSprite,
        uint tags,
        IGraphicsSpriteBackend? backend,
        out bool success)
    {
        success = false;
        if (!ValidateViewport(memory, viewPort) ||
            !ProbeExtSprite(memory, oldSprite) ||
            !ProbeExtSprite(memory, newSprite) ||
            !TryReadTags(memory, tags, out var tagValues) ||
            !TryReadExtSpriteOptions(
                tagValues,
                out _,
                out var attachedSprite,
                out var softSprite,
                out var scanDoubled))
        {
            return true;
        }

        if (softSprite || scanDoubled)
        {
            return backend?.TryChangeExtSprite(
                       viewPort,
                       oldSprite,
                       newSprite,
                       tags,
                       attachedSprite,
                       softSprite,
                       scanDoubled,
                       out success) == true;
        }

        success = ChangeExtSpriteA(memory, viewPort, oldSprite, newSprite, tags);
        return true;
    }

    /// <summary>Releases only ExtSprite/image storage owned by this registry.</summary>
    internal static void FreeSpriteData(
        IGraphicsAllocatorBackend allocator,
        Registry registry,
        uint extSprite)
    {
        if (!registry.TryTakeExtended(extSprite, out var allocation))
            return;

        allocator.Free(allocation.Image, allocation.ImageBytes, allocation.ImageClass);
        allocator.Free(extSprite, (uint)GraphicsLayouts.ExtSpriteSize, GraphicsMemoryClass.Public);
    }

    private static bool TryReadGetExtSpriteRequest(
        IGraphicsMemory memory,
        uint extSprite,
        uint tags,
        out short requested,
        out uint attachedSprite,
        out bool softSprite,
        out bool scanDoubled)
    {
        requested = -1;
        attachedSprite = 0;
        softSprite = false;
        scanDoubled = false;
        if (!ProbeExtSprite(memory, extSprite) ||
            !TryReadTags(memory, tags, out var tagValues) ||
            !TryReadExtSpriteOptions(
                tagValues,
                out requested,
                out attachedSprite,
                out softSprite,
                out scanDoubled))
        {
            return false;
        }

        return attachedSprite == 0 ||
               attachedSprite != extSprite &&
               ProbeExtSprite(memory, attachedSprite) &&
               !RangesOverlap(
                   extSprite,
                   (uint)GraphicsLayouts.ExtSpriteSize,
                   attachedSprite,
                   (uint)GraphicsLayouts.ExtSpriteSize);
    }

    private static bool ValidateViewport(IGraphicsMemory memory, uint viewPort)
        => viewPort == 0 ||
           (viewPort & 1u) == 0 &&
           ProbeRange(memory, viewPort, (uint)GraphicsLayouts.ViewPortSize);

    private static int ReserveSprite(Registry registry, short requested)
    {
        if (requested == -1)
        {
            for (var index = 0; index < registry.Allocated.Length; index++)
            {
                if (!registry.Allocated[index])
                {
                    registry.Allocated[index] = true;
                    return index;
                }
            }

            return GraphicsRasterOperations.Failure;
        }

        if (requested < 0 || requested >= registry.Allocated.Length || registry.Allocated[requested])
            return GraphicsRasterOperations.Failure;

        registry.Allocated[requested] = true;
        return requested;
    }

    private static int ReserveSpritePair(
        Registry registry,
        short requested,
        out int second)
    {
        second = -1;
        var first = requested == -1
            ? FindFreeSpritePair(registry)
            : requested;
        if (first < 0 ||
            first > registry.Allocated.Length - 2 ||
            (first & 1) != 0 ||
            registry.Allocated[first] ||
            registry.Allocated[first + 1])
        {
            return GraphicsRasterOperations.Failure;
        }

        registry.Allocated[first] = true;
        registry.Allocated[first + 1] = true;
        second = first + 1;
        return first;
    }

    private static int FindFreeSpritePair(Registry registry)
    {
        for (var first = 0; first < registry.Allocated.Length - 1; first += 2)
        {
            if (!registry.Allocated[first] && !registry.Allocated[first + 1])
                return first;
        }

        return GraphicsRasterOperations.Failure;
    }

    private static bool TryPublishAttachedPair(
        IGraphicsMemory memory,
        uint firstSprite,
        uint secondSprite,
        ushort firstNumber,
        ushort secondNumber)
    {
        if (!TrySnapshot(memory, firstSprite, GraphicsLayouts.ExtSpriteSize, out var firstOriginal) ||
            !TrySnapshot(memory, secondSprite, GraphicsLayouts.ExtSpriteSize, out var secondOriginal) ||
            !memory.TryReadLong(
                secondSprite + (uint)GraphicsLayouts.SimpleSpritePosCtlData,
                out var secondPosCtlData))
        {
            return false;
        }

        byte[] controlOriginal = Array.Empty<byte>();
        if (secondPosCtlData > uint.MaxValue - 3u)
            return false;

        if (secondPosCtlData != 0 &&
            !TrySnapshot(memory, secondPosCtlData, 4, out controlOriginal))
        {
            return false;
        }

        var committed =
            memory.TryWriteWord(
                firstSprite + (uint)GraphicsLayouts.SimpleSpriteNum,
                firstNumber) &&
            memory.TryWriteWord(
                secondSprite + (uint)GraphicsLayouts.SimpleSpriteNum,
                secondNumber) &&
            memory.TryReadWord(
                secondSprite + (uint)GraphicsLayouts.ExtSpriteFlags,
                out var flags) &&
            memory.TryWriteWord(
                secondSprite + (uint)GraphicsLayouts.ExtSpriteFlags,
                (ushort)(flags | GraphicsLayouts.SpriteAttachedFlag));

        if (committed && secondPosCtlData != 0 &&
            memory.TryReadWord(secondPosCtlData + 2u, out var control))
        {
            committed = memory.TryWriteWord(
                secondPosCtlData + 2u,
                (ushort)(control | GraphicsLayouts.SpriteAttachedFlag));
        }

        if (committed)
            return true;

        Restore(memory, firstSprite, firstOriginal);
        Restore(memory, secondSprite, secondOriginal);
        if (secondPosCtlData != 0)
            Restore(memory, secondPosCtlData, controlOriginal);
        return false;
    }

    private static bool TryPublishUnassignedSprites(
        IGraphicsMemory memory,
        uint firstSprite,
        uint secondSprite)
    {
        var firstAddress = firstSprite + (uint)GraphicsLayouts.SimpleSpriteNum;
        if (!TrySnapshot(memory, firstAddress, sizeof(ushort), out var firstOriginal))
            return false;

        var secondAddress = 0u;
        byte[] secondOriginal = Array.Empty<byte>();
        if (secondSprite != 0)
        {
            secondAddress = secondSprite + (uint)GraphicsLayouts.SimpleSpriteNum;
            if (!TrySnapshot(memory, secondAddress, sizeof(ushort), out secondOriginal))
                return false;
        }

        var committed = memory.TryWriteWord(firstAddress, DefaultUnassignedSprite);
        if (committed && secondSprite != 0)
            committed = memory.TryWriteWord(secondAddress, DefaultUnassignedSprite);

        if (committed)
            return true;

        // A rejected WORD may already have published one byte.  Restore both
        // public numbers byte-wise so an attached pair cannot expose one
        // failed sentinel and one caller-owned value.
        Restore(memory, firstAddress, firstOriginal);
        if (secondSprite != 0)
            Restore(memory, secondAddress, secondOriginal);
        return false;
    }

    private static bool TryReadSourceBitmap(
        IGraphicsMemory memory,
        uint bitMap,
        out SourceBitmap source)
    {
        source = default;
        if (bitMap == 0 ||
            (bitMap & 1u) != 0 ||
            !ProbeRange(
                memory,
                bitMap,
                (uint)(GraphicsLayouts.BitMapDepth + sizeof(byte))) ||
            !memory.TryReadWord(bitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, out var bytesPerRow) ||
            !memory.TryReadWord(bitMap + (uint)GraphicsLayouts.BitMapRows, out var rows) ||
            !memory.TryReadByte(bitMap + (uint)GraphicsLayouts.BitMapDepth, out var depth) ||
            bytesPerRow == 0 || rows == 0 || depth == 0 || depth > 8)
        {
            return false;
        }

        // BMF_MINPLANES-style BitMaps allocate only Depth plane links.  The
        // sprite source path consumes no bytes in the unused eight-plane
        // tail, so probe exactly the declared envelope before reading them.
        var structureBytes = GraphicsLayouts.BitMapPlanes + (depth * sizeof(uint));
        if (!ProbeRange(memory, bitMap, (uint)structureBytes))
            return false;

        var planes = new uint[8];
        for (var plane = 0; plane < depth; plane++)
        {
            if (!memory.TryReadLong(
                    bitMap + (uint)GraphicsLayouts.BitMapPlanes + (uint)(plane * 4),
                    out planes[plane]) ||
                planes[plane] == 0)
            {
                return false;
            }
        }

        source = new SourceBitmap(bytesPerRow, rows, depth, planes);
        return true;
    }

    private static bool TrySnapshot(
        IGraphicsMemory memory,
        uint address,
        int size,
        out byte[] original)
    {
        original = Array.Empty<byte>();
        if (address == 0 || size <= 0 ||
            address > uint.MaxValue - (uint)(size - 1))
            return false;

        original = new byte[size];
        for (var offset = 0; offset < size; offset++)
        {
            if (!memory.TryReadByte(address + (uint)offset, out original[offset]))
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
        for (var offset = 0; offset < original.Length; offset++)
            _ = memory.TryWriteByte(address + (uint)offset, original[offset]);
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

    private static bool TryBuildSpriteWords(
        IGraphicsMemory memory,
        SourceBitmap source,
        SpriteOptions options,
        ushort wordWidth,
        uint outputHeight,
        out ushort[] words)
    {
        var wordsPerRow = checked(wordWidth * 2);
        words = new ushort[checked((int)outputHeight * wordsPerRow)];
        var plane0 = options.Attached ? 2 : 0;
        var plane1 = plane0 + 1;
        for (var row = 0u; row < outputHeight; row++)
        {
            if (!TryMapReplicationCoordinate((int)row, options.YReplication, out var sourceRow))
                return false;

            for (var x = 0u; x < options.Width; x++)
            {
                if (!TryMapReplicationCoordinate(checked((int)x), options.XReplication, out var sourceX))
                    return false;

                // A source bitmap narrower than the requested hardware sprite
                // is defined to produce transparent right padding.  The same
                // rule applies to an output row that maps beyond the source
                // after a bounded replication/sub-sampling request.
                if (sourceX >= source.Width || sourceRow >= source.Rows)
                    continue;

                if (!TryReadSourceBit(memory, source, plane0, sourceX, sourceRow, out var lowBit) ||
                    !TryReadSourceBit(memory, source, plane1, sourceX, sourceRow, out var highBit))
                {
                    return false;
                }

                var wordIndex = checked((int)(row * wordsPerRow) + ((int)x / SpriteWordPixels) * 2);
                var mask = (ushort)(0x8000 >> ((int)x & (SpriteWordPixels - 1)));
                if (highBit)
                    words[wordIndex] |= mask;
                if (lowBit)
                    words[wordIndex + 1] |= mask;
            }
        }

        return true;
    }

    private static bool TryBuildOldFormatSpriteWords(
        IGraphicsMemory memory,
        uint bitMap,
        SpriteOptions options,
        uint outputHeight,
        out ushort[] words)
    {
        var wordsPerRow = 2;
        words = new ushort[checked((int)outputHeight * wordsPerRow)];
        var widthMask = options.Width == SpriteWordPixels
            ? ushort.MaxValue
            : (ushort)(ushort.MaxValue << (SpriteWordPixels - (int)options.Width));

        for (var row = 0u; row < outputHeight; row++)
        {
            if (!TryMapReplicationCoordinate((int)row, options.YReplication, out var sourceRow))
                return false;

            // A negative replication factor can sample beyond the packed
            // source height.  Match the standard planar path's transparent
            // out-of-range behavior without reading past the preflighted
            // legacy rows.
            if (sourceRow >= outputHeight)
                continue;

            var sourceAddress = (ulong)bitMap + 4ul + ((ulong)sourceRow * 4ul);
            if (sourceAddress > uint.MaxValue ||
                !memory.TryReadWord((uint)sourceAddress, out var sourcePlane0) ||
                !memory.TryReadWord((uint)(sourceAddress + 2ul), out var sourcePlane1))
            {
                return false;
            }

            sourcePlane0 &= widthMask;
            sourcePlane1 &= widthMask;
            for (var x = 0u; x < SpriteWordPixels; x++)
            {
                if (!TryMapReplicationCoordinate((int)x, options.XReplication, out var sourceX) ||
                    sourceX >= SpriteWordPixels)
                {
                    continue;
                }

                var sourceMask = (ushort)(0x8000 >> sourceX);
                var destinationMask = (ushort)(0x8000 >> (int)x);
                var wordIndex = checked((int)(row * (uint)wordsPerRow));

                // The public sprite-image envelope stores the high/second
                // plane first, matching the existing standard-planar path.
                if ((sourcePlane1 & sourceMask) != 0)
                    words[wordIndex] |= destinationMask;
                if ((sourcePlane0 & sourceMask) != 0)
                    words[wordIndex + 1] |= destinationMask;
            }
        }

        return true;
    }

    private static bool IsValidReplication(int replication)
        => replication >= -MaximumReplication && replication <= MaximumReplication;

    private static bool TryMapReplicationCoordinate(
        int outputCoordinate,
        int replication,
        out int sourceCoordinate)
    {
        sourceCoordinate = 0;
        if (!IsValidReplication(replication) || outputCoordinate < 0)
            return false;

        if (replication >= 0)
        {
            sourceCoordinate = outputCoordinate >> replication;
            return true;
        }

        sourceCoordinate = checked(outputCoordinate << -replication);
        return true;
    }

    private static bool TryReadSourceBit(
        IGraphicsMemory memory,
        SourceBitmap source,
        int plane,
        int x,
        int y,
        out bool set)
    {
        set = false;
        if (plane < 0 || plane >= source.Depth || x < 0 || y < 0 ||
            x >= source.Width || y >= source.Rows)
        {
            return false;
        }

        var byteOffset = ((ulong)y * source.BytesPerRow) + (uint)(x >> 3);
        var address = (ulong)source.Planes[plane] + byteOffset;
        if (address > uint.MaxValue ||
            !memory.TryReadByte((uint)address, out var value))
        {
            return false;
        }

        set = (value & (0x80 >> (x & 7))) != 0;
        return true;
    }

    private static bool WriteSpriteData(
        IGraphicsMemory memory,
        uint extSprite,
        uint image,
        uint outputHeight,
        ushort wordWidth,
        SpriteOptions options,
        ushort[] words)
    {
        return memory.TryWriteLong(
                extSprite + (uint)GraphicsLayouts.SimpleSpritePosCtlData,
                image) &&
            memory.TryWriteWord(
                extSprite + (uint)GraphicsLayouts.SimpleSpriteHeight,
                (ushort)outputHeight) &&
            memory.TryWriteWord(
                extSprite + (uint)GraphicsLayouts.SimpleSpriteNum,
                DefaultUnassignedSprite) &&
            memory.TryWriteWord(
                extSprite + (uint)GraphicsLayouts.ExtSpriteWordWidth,
                wordWidth) &&
            memory.TryWriteWord(
                extSprite + (uint)GraphicsLayouts.ExtSpriteFlags,
                options.Attached ? GraphicsLayouts.SpriteAttachedFlag : (ushort)0) &&
            WriteWords(memory, image + (uint)GraphicsLayouts.SpriteImagePosCtlSize, words) &&
            (!options.Attached || TryWriteAttachmentBits(memory, image, wordWidth));
    }

    private static bool TryWriteAttachmentBits(
        IGraphicsMemory memory,
        uint image,
        ushort wordWidth)
    {
        // The V39 data-conversion bug is avoided by publishing the attach bit
        // in the control word and in the documented wide-sprite words:
        // 2nd/3rd/5th for 16/32/64-pixel forms.  V40 keeps these bits
        // harmlessly set.  Preserve any converted data bit already present.
        if (!TrySetAttachmentBit(memory, image + 2u))
            return false;
        if (wordWidth >= 2 && !TrySetAttachmentBit(memory, image + 4u))
            return false;
        if (wordWidth >= 4 && !TrySetAttachmentBit(memory, image + 8u))
            return false;

        return true;
    }

    private static bool TrySetAttachmentBit(IGraphicsMemory memory, uint address)
        => memory.TryReadWord(address, out var value) &&
           memory.TryWriteWord(address, (ushort)(value | GraphicsLayouts.SpriteAttachedFlag));

    private static bool TryProbeWritableWord(IGraphicsMemory memory, uint address)
        => memory.TryReadWord(address, out var value) &&
           memory.TryWriteWord(address, value);

    private static bool WriteWords(IGraphicsMemory memory, uint address, ushort[] words)
    {
        for (var index = 0; index < words.Length; index++)
        {
            if (!memory.TryWriteWord(address + (uint)(index * 2), words[index]))
                return false;
        }

        return true;
    }

    private static bool TryReadExtSprite(
        IGraphicsMemory memory,
        uint extSprite,
        out ExtSpriteState state)
    {
        state = default;
        if (!ProbeExtSprite(memory, extSprite) ||
            !memory.TryReadLong(extSprite + (uint)GraphicsLayouts.SimpleSpritePosCtlData, out var posCtlData) ||
            !memory.TryReadWord(extSprite + (uint)GraphicsLayouts.SimpleSpriteHeight, out var height) ||
            !memory.TryReadWord(extSprite + (uint)GraphicsLayouts.SimpleSpriteX, out var x) ||
            !memory.TryReadWord(extSprite + (uint)GraphicsLayouts.SimpleSpriteY, out var y) ||
            !memory.TryReadWord(extSprite + (uint)GraphicsLayouts.SimpleSpriteNum, out var num) ||
            !memory.TryReadWord(extSprite + (uint)GraphicsLayouts.ExtSpriteWordWidth, out var wordWidth) ||
            !memory.TryReadWord(extSprite + (uint)GraphicsLayouts.ExtSpriteFlags, out var flags))
        {
            return false;
        }

        state = new ExtSpriteState(posCtlData, height, x, y, num, wordWidth, flags);
        return true;
    }

    private static bool ProbeExtSprite(IGraphicsMemory memory, uint extSprite)
        => extSprite != 0 &&
           (extSprite & 1u) == 0 &&
           ProbeRange(memory, extSprite, (uint)GraphicsLayouts.ExtSpriteSize);

    private static bool RangesOverlap(
        uint first,
        uint firstBytes,
        uint second,
        uint secondBytes)
    {
        var firstEnd = (ulong)first + firstBytes;
        var secondEnd = (ulong)second + secondBytes;
        return (ulong)first < secondEnd && (ulong)second < firstEnd;
    }

    private static bool ProbeSpriteImage(
        IGraphicsMemory memory,
        uint image,
        ushort height,
        ushort wordWidth)
    {
        if (image == 0 || wordWidth == 0 || height == 0)
            return false;

        var byteCount = 8ul + ((ulong)height * wordWidth * 4ul);
        return byteCount <= uint.MaxValue &&
               (memory is not IGraphicsDisplayMemory displayMemory ||
                displayMemory.IsDisplayDmaRange(image, (uint)byteCount)) &&
               ProbeRange(memory, image, (uint)byteCount);
    }

    private static bool TryReadSpriteOptions(
        Dictionary<uint, uint> tags,
        out SpriteOptions options)
    {
        options = new SpriteOptions
        {
            Width = GetTag(tags, GraphicsLayouts.SpriteAWidth, DefaultSpriteWidth),
            OutputHeight = GetTag(tags, GraphicsLayouts.SpriteAOutputHeight, 0),
            Attached = GetTag(tags, GraphicsLayouts.SpriteAAttached, 0) != 0,
            OldDataFormat = GetTag(tags, GraphicsLayouts.SpriteAOldDataFormat, 0) != 0,
            XReplication = unchecked((int)GetTag(tags, GraphicsLayouts.SpriteAXReplication, 0)),
            YReplication = unchecked((int)GetTag(tags, GraphicsLayouts.SpriteAYReplication, 0))
        };
        return options.Width != 0;
    }

    private static bool TryReadExtSpriteOptions(
        Dictionary<uint, uint> tags,
        out short requested,
        out uint attachedSprite,
        out bool softSprite,
        out bool scanDoubled)
    {
        var rawRequested = GetTag(tags, GraphicsLayouts.GsTagSpriteNum, uint.MaxValue);
        requested = rawRequested == uint.MaxValue
            ? (short)-1
            : unchecked((short)rawRequested);
        attachedSprite = GetTag(tags, GraphicsLayouts.GsTagAttached, 0);
        softSprite = GetTag(tags, GraphicsLayouts.GsTagSoftSprite, 0) != 0;
        scanDoubled = GetTag(tags, GraphicsLayouts.GsTagScanDoubled, 0) != 0;
        return requested == -1 || (requested >= 0 && requested < 8);
    }

    private static bool TryGetSpriteWordWidth(uint width, out ushort wordWidth)
    {
        wordWidth = 0;
        if (width is not (16u or 32u or MaximumHardwareSpriteWidth))
            return false;

        wordWidth = (ushort)(width / SpriteWordPixels);
        return true;
    }

    private static bool IsSupportedWordWidth(ushort wordWidth)
        => wordWidth is >= 1 and <= (MaximumHardwareSpriteWidth / SpriteWordPixels) &&
           (wordWidth & (wordWidth - 1)) == 0;

    private static uint GetTag(Dictionary<uint, uint> tags, uint tag, uint fallback)
        => tags.TryGetValue(tag, out var value) ? value : fallback;

    private static bool TryReadTags(
        IGraphicsMemory memory,
        uint address,
        out Dictionary<uint, uint> tags)
    {
        tags = new Dictionary<uint, uint>();
        if (address == 0)
            return true;

        var visited = new HashSet<uint>();
        for (var count = 0; count < MaximumTagItems && address != 0; count++)
        {
            if ((address & 1u) != 0 ||
                !visited.Add(address) ||
                // A TagItem occupies bytes [address..address+7].  The
                // final aligned guest start, $FFFF_FFF8, is complete; only
                // starts above max-7 cross the 32-bit address boundary.
                address > uint.MaxValue - 7u ||
                !memory.TryReadLong(address, out var tag) ||
                !memory.TryReadLong(address + 4u, out var data))
            {
                return false;
            }

            var nextAddress = address + 8u;
            switch (tag)
            {
                case TagDone:
                    return true;
                case TagIgnore:
                    if (nextAddress < address)
                        return false;

                    address = nextAddress;
                    continue;
                case TagMore:
                    if (data == 0)
                        // Utility.library's NextTagItem treats a null
                        // continuation as the clean end of the tag chain.
                        // Preserve that shared Exec contract for sprite
                        // option lists instead of requiring a redundant
                        // TAG_DONE item after TAG_MORE.
                        return true;

                    if ((data & 1u) != 0)
                        return false;

                    address = data;
                    continue;
                case TagSkip:
                {
                    var skipBytes = (ulong)data * 8ul;
                    if (skipBytes > uint.MaxValue ||
                        nextAddress < address ||
                        nextAddress > uint.MaxValue - (uint)skipBytes)
                        return false;

                    address = nextAddress + (uint)skipBytes;
                    continue;
                }
                default:
                    if (nextAddress < address)
                        return false;

                    tags[tag] = data;
                    address = nextAddress;
                    continue;
            }
        }

        return address == 0;
    }

    private static bool TryClearRange(IGraphicsMemory memory, uint address, uint byteCount)
    {
        if (!ProbeRange(memory, address, byteCount))
            return false;

        for (var offset = 0u; offset < byteCount; offset++)
        {
            if (!memory.TryWriteByte(address + offset, 0))
                return false;
        }

        return true;
    }

    private readonly record struct SourceBitmap(
        ushort BytesPerRow,
        ushort Rows,
        byte Depth,
        uint[] Planes)
    {
        internal int Width => BytesPerRow * 8;
    }

    private readonly record struct SpriteOptions
    {
        internal uint Width { get; init; }
        internal int XReplication { get; init; }
        internal int YReplication { get; init; }
        internal uint OutputHeight { get; init; }
        internal bool Attached { get; init; }
        internal bool OldDataFormat { get; init; }
    }

    private readonly record struct ExtSpriteState(
        uint PosCtlData,
        ushort Height,
        ushort X,
        ushort Y,
        ushort Num,
        ushort WordWidth,
        ushort Flags);

    private static bool ProbeRange(IGraphicsMemory memory, uint address, uint byteCount)
    {
        if (!IsEvenAddress(address) || byteCount <= 0 ||
            address > uint.MaxValue - (byteCount - 1))
        {
            return false;
        }

        for (var offset = 0u; offset < byteCount; offset++)
        {
            if (!memory.TryReadByte(address + (uint)offset, out _))
                return false;
        }

        return true;
    }

    private static bool IsEvenAddress(uint address)
        => address != 0 && (address & 1u) == 0;

    private static bool ProbeSprite(IGraphicsMemory memory, uint sprite)
    {
        // SimpleSprite contains word/long fields and is consumed through
        // native 68k aligned accesses.  Keep an odd guest envelope for the
        // native/provider boundary instead of byte-processing it here.
        if ((sprite & 1u) != 0 ||
            sprite > uint.MaxValue - (uint)(GraphicsLayouts.SimpleSpriteSize - 1))
            return false;

        for (var offset = 0; offset < GraphicsLayouts.SimpleSpriteSize; offset++)
        {
            if (!memory.TryReadByte(sprite + (uint)offset, out _))
                return false;
        }

        return true;
    }
}
