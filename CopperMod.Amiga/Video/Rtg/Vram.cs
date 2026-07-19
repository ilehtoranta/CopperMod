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
        private readonly byte[]?[] _pages;
        private readonly int[] _pageOwners;
        private readonly bool[] _dirtyPages;
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
            _pages = budget == 0 ? Array.Empty<byte[]?>() : new byte[PageCount][];
            _pageOwners = budget == 0 ? Array.Empty<int>() : new int[PageCount];
            _dirtyPages = budget == 0 ? Array.Empty<bool>() : new bool[PageCount];
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

        internal byte[]? GetDirectReadPage(int pageIndex)
            => _pages[pageIndex];

        internal byte[] GetDirectWritePage(int pageIndex)
            => GetOrCreatePage(pageIndex);

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

    internal interface IAmigaRtgFirmwareProvider
    {
        AutoconfigIdentity Identity { get; }

        void Attach(AmigaBus bus);

        byte ReadBoardByte(int offset);

        void OnConfigured(uint baseAddress);

        void Reset(bool cold);
    }

    internal sealed class AutoconfigRtgBoard : AutoconfigBoard
    {
        public const ushort ManufacturerId = 0x07DB;
        public const byte ProductId = 0x4A;
        public const int BoardSize = 64 * 1024;

        private readonly RtgVramBackend _vram;
        private IAmigaRtgFirmwareProvider? _firmware;

        public AutoconfigRtgBoard(RtgVramBackend vram)
            : base(AutoconfigIdentity.CreateIoBoard(BoardSize, ManufacturerId, ProductId, 0))
            => _vram = vram ?? throw new ArgumentNullException(nameof(vram));

        public override bool IsPresent => _vram.IsPresent;

        public bool HasFirmware => _firmware != null;

        public void AttachFirmware(AmigaBus bus, IAmigaRtgFirmwareProvider firmware)
        {
            ArgumentNullException.ThrowIfNull(bus);
            ArgumentNullException.ThrowIfNull(firmware);
            if (_firmware != null)
            {
                throw new InvalidOperationException("RTG firmware is already attached.");
            }

            if (IsConfigured || IsShutUp)
            {
                throw new InvalidOperationException("RTG firmware must be attached before Autoconfig begins.");
            }

            _firmware = firmware;
            Identity = firmware.Identity;
            firmware.Attach(bus);
        }

        public void RefreshFirmwareAttachment(AmigaBus bus)
            => _firmware?.Attach(bus);

        public override bool ContainsBoardAddress(uint address)
            => IsPresent && IsConfigured && address >= ConfiguredBase && address - ConfiguredBase < BoardSize;

        public override byte ReadBoardByte(uint address)
            => _firmware?.ReadBoardByte(checked((int)(address - ConfiguredBase))) ?? 0;

        public override bool TryWriteBoardByte(uint address, byte value)
        {
            _ = value;
            return ContainsBoardAddress(address);
        }

        public override void ResetConfiguration()
        {
            _vram.Deactivate(clear: true);
            _firmware?.Reset(cold: false);
            base.ResetConfiguration();
        }

        public override void ColdReset()
        {
            _vram.Deactivate(clear: true);
            _firmware?.Reset(cold: true);
            base.ResetConfiguration();
        }

        protected override void OnConfigured(uint baseAddress)
            => _firmware?.OnConfigured(baseAddress);
    }
}
