using System;
using System.Collections.Generic;

namespace CopperMod.Cust
{
    internal enum HunkMemoryKind
    {
        Any,
        Chip,
        Fast
    }

    internal enum HunkSegmentKind
    {
        Code,
        Data,
        Bss
    }

    internal sealed class HunkFile
    {
        public HunkFile(IReadOnlyList<HunkSegment> segments)
        {
            Segments = segments ?? throw new ArgumentNullException(nameof(segments));
        }

        public IReadOnlyList<HunkSegment> Segments { get; }
    }

    internal sealed class HunkSegment
    {
        public HunkSegment(
            int index,
            HunkSegmentKind kind,
            HunkMemoryKind memoryKind,
            int declaredSizeBytes,
            byte[] data,
            IReadOnlyList<HunkRelocationBlock> relocations)
        {
            Index = index;
            Kind = kind;
            MemoryKind = memoryKind;
            DeclaredSizeBytes = declaredSizeBytes;
            Data = data ?? throw new ArgumentNullException(nameof(data));
            Relocations = relocations ?? throw new ArgumentNullException(nameof(relocations));
        }

        public int Index { get; }

        public HunkSegmentKind Kind { get; }

        public HunkMemoryKind MemoryKind { get; }

        public int DeclaredSizeBytes { get; }

        public byte[] Data { get; }

        public IReadOnlyList<HunkRelocationBlock> Relocations { get; }
    }

    internal sealed class HunkRelocationBlock
    {
        public HunkRelocationBlock(int targetSegmentIndex, IReadOnlyList<int> offsets)
        {
            TargetSegmentIndex = targetSegmentIndex;
            Offsets = offsets ?? throw new ArgumentNullException(nameof(offsets));
        }

        public int TargetSegmentIndex { get; }

        public IReadOnlyList<int> Offsets { get; }
    }
}
