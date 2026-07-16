/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections.Generic;

namespace CopperMod.Amiga.Video.Rtg
{
    internal sealed class RtgVramBackend
    {
        public const uint BaseAddress = 0x8000_0000u;
        public const ulong EndExclusive = 0x1_0000_0000UL;
        public const int PageShift = 16;
        public const int PageSize = 1 << PageShift;
        public const int PageCount = 1 << (32 - PageShift - 1);

        private readonly long _budget;
        private readonly byte[]?[] _pages = new byte[PageCount][];
        private readonly int[] _pageOwners = new int[PageCount];
        private readonly bool[] _dirtyPages = new bool[PageCount];
        private readonly Dictionary<int, Allocation> _allocations = new Dictionary<int, Allocation>();
        private readonly Dictionary<int, uint> _codePageGenerations = new Dictionary<int, uint>();
        private int _nextAllocationId = 1;
        private long _reservedBytes;

        public RtgVramBackend(long budget)
        {
            if (budget < 0 || budget > (long)(EndExclusive - BaseAddress))
            {
                throw new ArgumentOutOfRangeException(nameof(budget), budget, "RTG VRAM must be between 0 and 2 GiB.");
            }

            _budget = budget;
            Array.Fill(_pageOwners, -1);
        }

        public event Action? AddressMapChanged;

        public event Action<uint, int>? Written;

        public bool IsPresent => _budget != 0;

        public bool Active { get; private set; }

        public long Budget => _budget;

        public long ReservedBytes => _reservedBytes;

        public int CommittedPageCount { get; private set; }

        public void Activate()
        {
            if (IsPresent && !Active)
            {
                Active = true;
                AddressMapChanged?.Invoke();
            }
        }

        public void Deactivate(bool clear)
        {
            var changed = Active || (clear && _allocations.Count != 0);
            Active = false;
            if (clear)
            {
                Clear();
            }

            if (changed)
            {
                AddressMapChanged?.Invoke();
            }
        }

        public uint Allocate(long byteCount)
        {
            if (!Active || byteCount <= 0 || byteCount > _budget)
            {
                return 0;
            }

            var rounded = AlignToPage(byteCount);
            if (rounded > _budget - _reservedBytes)
            {
                return 0;
            }

            var pageCount = checked((int)(rounded >> PageShift));
            var firstPage = FindFreePages(pageCount);
            if (firstPage < 0)
            {
                return 0;
            }

            var id = _nextAllocationId++;
            if (id <= 0)
            {
                throw new AmigaEmulationException("RTG VRAM allocation id space exhausted.");
            }

            for (var page = firstPage; page < firstPage + pageCount; page++)
            {
                _pageOwners[page] = id;
            }

            var address = BaseAddress + ((uint)firstPage << PageShift);
            _allocations.Add(id, new Allocation(id, address, byteCount, rounded, firstPage, pageCount));
            _reservedBytes += rounded;
            AddressMapChanged?.Invoke();
            return address;
        }

        public bool Free(uint address)
        {
            if (!TryGetAllocationByBase(address, out var allocation))
            {
                return false;
            }

            for (var page = allocation.FirstPage; page < allocation.FirstPage + allocation.PageCount; page++)
            {
                _pageOwners[page] = -1;
                if (_pages[page] != null)
                {
                    _pages[page] = null;
                    CommittedPageCount--;
                }

                _dirtyPages[page] = false;
                RemoveCodeGenerationsForPage(page);
            }

            _allocations.Remove(allocation.Id);
            _reservedBytes -= allocation.RoundedBytes;
            AddressMapChanged?.Invoke();
            return true;
        }

        public bool IsAllocatedAddress(uint address)
        {
            if (!Active || address < BaseAddress)
            {
                return false;
            }

            return _pageOwners[GetPageIndex(address)] >= 0;
        }

        public bool IsAllocatedRange(uint address, int byteCount)
        {
            if (byteCount < 0 || !Active)
            {
                return false;
            }

            if (byteCount == 0)
            {
                return true;
            }

            var end = (ulong)address + (uint)byteCount;
            if (address < BaseAddress || end > EndExclusive)
            {
                return false;
            }

            var firstPage = GetPageIndex(address);
            var lastPage = GetPageIndex((uint)(end - 1));
            var owner = _pageOwners[firstPage];
            if (owner < 0)
            {
                return false;
            }

            for (var page = firstPage + 1; page <= lastPage; page++)
            {
                if (_pageOwners[page] != owner)
                {
                    return false;
                }
            }

            return true;
        }

        public bool TryReadByte(uint address, out byte value)
        {
            if (!IsAllocatedAddress(address))
            {
                value = 0;
                return false;
            }

            var page = _pages[GetPageIndex(address)];
            value = page == null ? (byte)0 : page[address & (PageSize - 1)];
            return true;
        }

        public bool TryWriteByte(uint address, byte value)
        {
            if (!IsAllocatedAddress(address))
            {
                return false;
            }

            var pageIndex = GetPageIndex(address);
            var page = GetOrCreatePage(pageIndex);
            page[address & (PageSize - 1)] = value;
            MarkWritten(address, 1);
            return true;
        }

        public bool TryGetContiguousReadMemory(uint address, int byteCount, out byte[] memory, out int offset)
        {
            memory = Array.Empty<byte>();
            offset = 0;
            if (!IsAllocatedRange(address, byteCount) || !FitsSinglePage(address, byteCount))
            {
                return false;
            }

            var page = _pages[GetPageIndex(address)];
            if (page == null)
            {
                return false;
            }

            memory = page;
            offset = (int)(address & (PageSize - 1));
            return true;
        }

        public bool TryGetContiguousWriteMemory(uint address, int byteCount, out byte[] memory, out int offset)
        {
            memory = Array.Empty<byte>();
            offset = 0;
            if (!IsAllocatedRange(address, byteCount) || !FitsSinglePage(address, byteCount))
            {
                return false;
            }

            memory = GetOrCreatePage(GetPageIndex(address));
            offset = (int)(address & (PageSize - 1));
            return true;
        }

        public void CompleteDirectWrite(uint address, int byteCount)
            => MarkWritten(address, byteCount);

        public uint GetCodePageGeneration(uint address, int generationPageShift)
        {
            var key = checked((int)((address - BaseAddress) >> generationPageShift));
            return _codePageGenerations.TryGetValue(key, out var generation) ? generation : 0;
        }

        public void SetCodePageGeneration(uint address, int generationPageShift, uint generation)
        {
            var key = checked((int)((address - BaseAddress) >> generationPageShift));
            _codePageGenerations[key] = generation;
        }

        public void ClearCodePageGenerations()
            => _codePageGenerations.Clear();

        public bool IsPageDirty(uint address)
            => address >= BaseAddress && _dirtyPages[GetPageIndex(address)];

        public void ClearDirtyPages()
            => Array.Clear(_dirtyPages);

        private byte[] GetOrCreatePage(int pageIndex)
        {
            var page = _pages[pageIndex];
            if (page != null)
            {
                return page;
            }

            try
            {
                page = GC.AllocateUninitializedArray<byte>(PageSize);
                Array.Clear(page);
            }
            catch (OutOfMemoryException exception)
            {
                throw new AmigaEmulationException(
                    $"Unable to commit a 64-KiB RTG VRAM page at ${BaseAddress + ((uint)pageIndex << PageShift):X8}.",
                    exception);
            }

            _pages[pageIndex] = page;
            CommittedPageCount++;
            AddressMapChanged?.Invoke();
            return page;
        }

        private void MarkWritten(uint address, int byteCount)
        {
            if (byteCount <= 0)
            {
                return;
            }

            var end = Math.Min(EndExclusive, (ulong)address + (uint)byteCount);
            var firstPage = GetPageIndex(address);
            var lastPage = GetPageIndex((uint)(end - 1));
            for (var page = firstPage; page <= lastPage; page++)
            {
                _dirtyPages[page] = true;
            }

            Written?.Invoke(address, byteCount);
        }

        private void Clear()
        {
            Array.Clear(_pages);
            Array.Fill(_pageOwners, -1);
            Array.Clear(_dirtyPages);
            _allocations.Clear();
            _codePageGenerations.Clear();
            _nextAllocationId = 1;
            _reservedBytes = 0;
            CommittedPageCount = 0;
        }

        private int FindFreePages(int count)
        {
            var runStart = 0;
            var runLength = 0;
            for (var page = 0; page < PageCount; page++)
            {
                if (_pageOwners[page] < 0)
                {
                    if (runLength == 0)
                    {
                        runStart = page;
                    }

                    runLength++;
                    if (runLength == count)
                    {
                        return runStart;
                    }
                }
                else
                {
                    runLength = 0;
                }
            }

            return -1;
        }

        private bool TryGetAllocationByBase(uint address, out Allocation allocation)
        {
            foreach (var candidate in _allocations.Values)
            {
                if (candidate.Address == address)
                {
                    allocation = candidate;
                    return true;
                }
            }

            allocation = default;
            return false;
        }

        private void RemoveCodeGenerationsForPage(int page)
        {
            const int generationPagesPerVramPage = PageSize / 4096;
            var first = page * generationPagesPerVramPage;
            for (var index = 0; index < generationPagesPerVramPage; index++)
            {
                _codePageGenerations.Remove(first + index);
            }
        }

        private static bool FitsSinglePage(uint address, int byteCount)
            => byteCount >= 0 &&
                (address & (PageSize - 1)) + (ulong)byteCount <= PageSize;

        private static int GetPageIndex(uint address)
            => checked((int)((address - BaseAddress) >> PageShift));

        private static long AlignToPage(long value)
            => checked((value + PageSize - 1) & -PageSize);

        private readonly record struct Allocation(
            int Id,
            uint Address,
            long RequestedBytes,
            long RoundedBytes,
            int FirstPage,
            int PageCount);
    }

    internal sealed class AutoconfigRtgBoard : AutoconfigBoard
    {
        public const ushort ManufacturerId = 0x07DB;
        public const byte ProductId = 0x4A;
        public const int BoardSize = 64 * 1024;
        public const ushort DiagnosticRomVector = 0x4000;

        internal const int DiagAreaOffset = 0x4000;
        internal const int DiagAreaCopySize = 0x1000;
        internal const int DiagPointOffset = 0x20;
        internal const int ResidentOffset = 0x40;
        internal const int NameOffset = 0x100;
        internal const int IdStringOffset = 0x120;
        internal const int ResidentInitOffset = 0x140;
        internal const int LibraryBaseOffset = 0x300;
        private const int ExecLibraryListOffset = 0x17A;

        private readonly RtgVramBackend _vram;
        private readonly byte[] _rom;
        private AmigaBus? _bootstrapBus;
        private CyberGraphicsLibrary? _library;
        private uint _diagCopyBase;

        public AutoconfigRtgBoard(RtgVramBackend vram)
            : base(AutoconfigIdentity.CreateIoBoard(BoardSize, ManufacturerId, ProductId, DiagnosticRomVector))
        {
            _vram = vram ?? throw new ArgumentNullException(nameof(vram));
            _rom = CreateBoardRom();
        }

        public override bool IsPresent => _vram.IsPresent;

        public override bool ContainsBoardAddress(uint address)
            => IsPresent && IsConfigured && address >= ConfiguredBase && address - ConfiguredBase < BoardSize;

        public override byte ReadBoardByte(uint address)
            => _rom[(int)(address - ConfiguredBase)];

        public override bool TryWriteBoardByte(uint address, byte value)
        {
            _ = value;
            return ContainsBoardAddress(address);
        }

        public bool ResidentInstalled { get; private set; }

        public uint LibraryBase { get; private set; }

        public void InstallBootstrapTraps(AmigaBus bus, CyberGraphicsLibrary library)
        {
            _bootstrapBus = bus ?? throw new ArgumentNullException(nameof(bus));
            _library = library ?? throw new ArgumentNullException(nameof(library));
            WriteTrap(DiagPointOffset, bus.RegisterRelocatableHostTrapStub(HostDiagBootstrap));
            WriteTrap(ResidentInitOffset, bus.RegisterRelocatableHostTrapStub(HostResidentInit));
        }

        public override void ResetConfiguration()
        {
            _vram.Deactivate(clear: true);
            ResidentInstalled = false;
            LibraryBase = 0;
            _diagCopyBase = 0;
            base.ResetConfiguration();
        }

        public override void ColdReset()
        {
            _vram.Deactivate(clear: true);
            ResidentInstalled = false;
            LibraryBase = 0;
            _diagCopyBase = 0;
            base.ResetConfiguration();
        }

        private void HostDiagBootstrap(M68kCpuState state)
        {
            var bus = _bootstrapBus;
            _diagCopyBase = state.A[2] & 0x00FF_FFFFu;
            if (bus != null && _diagCopyBase != 0 && bus.IsMappedMemoryRange(_diagCopyBase, DiagAreaCopySize))
            {
                var resident = _diagCopyBase + ResidentOffset;
                bus.WriteLong(resident + 0x02, resident);
                bus.WriteLong(resident + 0x06, _diagCopyBase + ResidentInitOffset + 4u);
                bus.WriteLong(resident + 0x0E, _diagCopyBase + NameOffset);
                bus.WriteLong(resident + 0x12, _diagCopyBase + IdStringOffset);
                bus.WriteLong(resident + 0x16, _diagCopyBase + ResidentInitOffset);
            }

            state.D[0] = 1;
        }

        private void HostResidentInit(M68kCpuState state)
        {
            var bus = _bootstrapBus;
            var library = _library;
            var execBase = state.A[6] != 0 ? state.A[6] : bus?.ReadLong(4) ?? 0;
            if (bus == null || library == null || _diagCopyBase == 0 || execBase == 0 ||
                !bus.IsMappedMemoryRange(_diagCopyBase, DiagAreaCopySize))
            {
                state.D[0] = 0;
                return;
            }

            LibraryBase = _diagCopyBase + LibraryBaseOffset;
            bus.ClearMemory(LibraryBase - 258, 258 + 0x22);
            bus.WriteByte(LibraryBase + 0x08, 9, 0); // NT_LIBRARY
            bus.WriteLong(LibraryBase + 0x0A, _diagCopyBase + NameOffset);
            bus.WriteWord(LibraryBase + 0x10, 258);
            bus.WriteWord(LibraryBase + 0x12, 0x22);
            bus.WriteWord(LibraryBase + 0x14, CyberGraphicsLibrary.Version);
            bus.WriteWord(LibraryBase + 0x16, CyberGraphicsLibrary.Revision);
            bus.WriteLong(LibraryBase + 0x18, _diagCopyBase + IdStringOffset);
            library.InstallTrapVectors(LibraryBase);
            LinkTail(bus, execBase + ExecLibraryListOffset, LibraryBase);
            library.InstallSystemPatches(execBase);
            ResidentInstalled = true;
            state.D[0] = LibraryBase;
        }

        private static void LinkTail(AmigaBus bus, uint list, uint node)
        {
            if (!bus.IsMappedMemoryRange(list, 14))
            {
                return;
            }

            if (bus.ReadLong(list) == 0 && bus.ReadLong(list + 8) == 0)
            {
                bus.WriteLong(list, list + 4);
                bus.WriteLong(list + 4, 0);
                bus.WriteLong(list + 8, list);
            }

            var tailPred = bus.ReadLong(list + 8);
            if (tailPred == 0)
            {
                tailPred = list;
            }

            bus.WriteLong(node, list + 4);
            bus.WriteLong(node + 4, tailPred);
            bus.WriteLong(tailPred == list ? list : tailPred, node);
            bus.WriteLong(list + 8, node);
        }

        private static byte[] CreateBoardRom()
        {
            var rom = new byte[BoardSize];
            rom[DiagAreaOffset] = 0x90;
            WriteUInt16(rom, DiagAreaOffset + 0x02, DiagAreaCopySize);
            WriteUInt16(rom, DiagAreaOffset + 0x04, DiagPointOffset);
            WriteUInt16(rom, DiagAreaOffset + 0x08, NameOffset);
            WriteReturnStub(rom, DiagPointOffset);
            WriteReturnStub(rom, ResidentInitOffset);
            var resident = DiagAreaOffset + ResidentOffset;
            WriteUInt16(rom, resident, 0x4AFC);
            WriteUInt32(rom, resident + 0x02, ResidentOffset);
            WriteUInt32(rom, resident + 0x06, ResidentInitOffset + 4u);
            rom[resident + 0x0A] = 1;
            rom[resident + 0x0B] = 1;
            rom[resident + 0x0C] = 9;
            rom[resident + 0x0D] = 20;
            WriteUInt32(rom, resident + 0x0E, NameOffset);
            WriteUInt32(rom, resident + 0x12, IdStringOffset);
            WriteUInt32(rom, resident + 0x16, ResidentInitOffset);
            WriteAscii(rom, DiagAreaOffset + NameOffset, CyberGraphicsLibrary.Name);
            WriteAscii(rom, DiagAreaOffset + IdStringOffset, "cybergraphics.library 52.1 (Copper RTG)");
            return rom;
        }

        private void WriteTrap(int relativeOffset, ushort trapId)
        {
            var offset = DiagAreaOffset + relativeOffset;
            WriteUInt16(_rom, offset, 0xFF00);
            WriteUInt16(_rom, offset + 2, trapId);
        }

        private static void WriteReturnStub(byte[] rom, int relativeOffset)
        {
            var offset = DiagAreaOffset + relativeOffset;
            rom[offset] = 0x70;
            rom[offset + 1] = 0x01;
            rom[offset + 2] = 0x4E;
            rom[offset + 3] = 0x75;
        }

        private static void WriteAscii(byte[] rom, int offset, string value)
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(value);
            Array.Copy(bytes, 0, rom, offset, bytes.Length);
            rom[offset + bytes.Length] = 0;
        }

        private static void WriteUInt16(byte[] target, int offset, int value)
        {
            target[offset] = (byte)(value >> 8);
            target[offset + 1] = (byte)value;
        }

        private static void WriteUInt32(byte[] target, int offset, uint value)
        {
            target[offset] = (byte)(value >> 24);
            target[offset + 1] = (byte)(value >> 16);
            target[offset + 2] = (byte)(value >> 8);
            target[offset + 3] = (byte)value;
        }
    }
}
