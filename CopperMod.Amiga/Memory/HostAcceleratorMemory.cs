/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System.Runtime.CompilerServices;

namespace CopperMod.Amiga.Memory
{
    internal enum HostAcceleratorMemoryKind
    {
        RealFastRam,
        RtgVram
    }

    internal sealed class HostAcceleratorDirectMemory
    {
        private readonly byte[]? _linearMemory;
        private readonly int _linearOffset;
        private readonly RtgVramBackend? _rtgVram;
        private int _cachedReadPageIndex = -1;
        private byte[]? _cachedReadPage;
        private int _cachedWritePageIndex = -1;
        private byte[]? _cachedWritePage;

        internal HostAcceleratorDirectMemory(byte[] memory, int offset)
        {
            _linearMemory = memory;
            _linearOffset = offset;
            Kind = HostAcceleratorMemoryKind.RealFastRam;
        }

        internal HostAcceleratorDirectMemory(RtgVramBackend rtgVram, uint baseAddress)
        {
            _rtgVram = rtgVram;
            BaseAddress = baseAddress;
            Kind = HostAcceleratorMemoryKind.RtgVram;
        }

        internal HostAcceleratorMemoryKind Kind { get; }

        internal uint BaseAddress { get; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal byte ReadByte(int offset)
        {
            if (_linearMemory != null)
            {
                return _linearMemory[_linearOffset + offset];
            }

            var address = BaseAddress + (uint)offset;
            var pageIndex = (int)((address - RtgVramBackend.BaseAddress) >> RtgVramBackend.PageShift);
            if (pageIndex != _cachedReadPageIndex || _cachedReadPage == null)
            {
                _cachedReadPageIndex = pageIndex;
                _cachedReadPage = _rtgVram!.GetDirectReadPage(pageIndex);
            }

            return _cachedReadPage?[address & (RtgVramBackend.PageSize - 1)] ?? 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void WriteByte(int offset, byte value)
        {
            if (_linearMemory != null)
            {
                _linearMemory[_linearOffset + offset] = value;
                return;
            }

            var address = BaseAddress + (uint)offset;
            var pageIndex = (int)((address - RtgVramBackend.BaseAddress) >> RtgVramBackend.PageShift);
            if (pageIndex != _cachedWritePageIndex)
            {
                _cachedWritePageIndex = pageIndex;
                _cachedWritePage = _rtgVram!.GetDirectWritePage(pageIndex);
                _cachedReadPageIndex = pageIndex;
                _cachedReadPage = _cachedWritePage;
            }

            _cachedWritePage![address & (RtgVramBackend.PageSize - 1)] = value;
        }
    }
}
