/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Runtime.InteropServices;

namespace CopperMod.Amiga.CustomChips.Denise
{
    internal readonly struct PresentationFrameTarget
    {
        private readonly int[]? _signedPixels;
        private readonly uint[]? _unsignedPixels;

        public PresentationFrameTarget(int[] pixels)
        {
            _signedPixels = pixels ?? throw new ArgumentNullException(nameof(pixels));
            _unsignedPixels = null;
        }

        public PresentationFrameTarget(uint[] pixels)
        {
            _unsignedPixels = pixels ?? throw new ArgumentNullException(nameof(pixels));
            _signedPixels = null;
        }

        public bool IsBound => _signedPixels != null || _unsignedPixels != null;

        public int Length => _unsignedPixels?.Length ?? _signedPixels?.Length ?? 0;

        public Span<uint> AsSpan()
        {
            if (_unsignedPixels != null)
            {
                return _unsignedPixels;
            }

            return _signedPixels != null
                ? MemoryMarshal.Cast<int, uint>(_signedPixels.AsSpan())
                : Span<uint>.Empty;
        }
    }
}
