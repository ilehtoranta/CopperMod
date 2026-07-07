/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;

namespace Copper68k
{
    /// <summary>
    /// Records the generation values for code pages captured by a JIT code snapshot.
    /// </summary>
    public readonly struct M68kCodeGenerationStamp
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="M68kCodeGenerationStamp"/> struct.
        /// </summary>
        /// <param name="pages">The normalized code page base addresses.</param>
        /// <param name="generations">The generation value for each page.</param>
        public M68kCodeGenerationStamp(uint[] pages, uint[] generations)
        {
            Pages = pages;
            Generations = generations;
        }

        /// <summary>
        /// Gets the normalized code page base addresses.
        /// </summary>
        public uint[] Pages { get; }

        /// <summary>
        /// Gets the generation value for each page in <see cref="Pages"/>.
        /// </summary>
        public uint[] Generations { get; }

        /// <summary>
        /// Gets a value indicating whether the stamp contains no page information.
        /// </summary>
        public bool IsEmpty => Pages == null || Generations == null || Pages.Length == 0;
    }

    /// <summary>
    /// Immutable byte snapshot used by the JIT compiler to decode code off the emulation thread.
    /// </summary>
    public readonly struct M68kJitCodeSnapshot
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="M68kJitCodeSnapshot"/> struct.
        /// </summary>
        /// <param name="root">The normalized root address captured by the snapshot.</param>
        /// <param name="bytes">The captured instruction bytes.</param>
        /// <param name="generationStamp">The generation stamp for pages covered by <paramref name="bytes"/>.</param>
        /// <param name="hostTrapStubAddresses">Host trap stub addresses inside the captured range.</param>
        public M68kJitCodeSnapshot(uint root, byte[] bytes, M68kCodeGenerationStamp generationStamp, uint[] hostTrapStubAddresses)
        {
            Root = root;
            Bytes = bytes;
            GenerationStamp = generationStamp;
            HostTrapStubAddresses = hostTrapStubAddresses;
        }

        /// <summary>
        /// Gets the normalized root address captured by the snapshot.
        /// </summary>
        public uint Root { get; }

        /// <summary>
        /// Gets the captured instruction bytes.
        /// </summary>
        public byte[] Bytes { get; }

        /// <summary>
        /// Gets the generation stamp for pages covered by <see cref="Bytes"/>.
        /// </summary>
        public M68kCodeGenerationStamp GenerationStamp { get; }

        /// <summary>
        /// Gets host trap stub addresses inside the captured range.
        /// </summary>
        public uint[] HostTrapStubAddresses { get; }

        /// <summary>
        /// Gets a value indicating whether the snapshot contains no bytes.
        /// </summary>
        public bool IsEmpty => Bytes == null || Bytes.Length == 0;
    }

    internal sealed class M68kSnapshotCodeReader : IM68kCodeReader
    {
        private const int CodeGenerationPageSize = 1 << 8;
        private readonly M68kJitCodeSnapshot _snapshot;

        public M68kSnapshotCodeReader(M68kJitCodeSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public uint Root => _snapshot.Root;

        public int ByteLength => _snapshot.Bytes?.Length ?? 0;

        public bool HasHostTrapStub(uint address)
        {
            address = NormalizeAddress(address);
            if (!TryGetOffset(address, 2, out _))
            {
                return true;
            }

            return false;
        }

        public ushort ReadHostWord(uint address)
        {
            if (!TryGetOffset(address, 2, out var offset))
            {
                throw M68kCodeReadException.Instance;
            }

            if (IsCapturedHostTrapStub(address))
            {
                return 0x4AFC;
            }

            var bytes = _snapshot.Bytes;
            return (ushort)((bytes[offset] << 8) | bytes[offset + 1]);
        }

        public bool TryCaptureWords(uint address, int byteLength, out ushort[] words)
        {
            words = Array.Empty<ushort>();
            if (byteLength <= 0 || !TryGetOffset(address, byteLength, out var offset))
            {
                return false;
            }

            var wordCount = (byteLength + 1) / 2;
            words = new ushort[wordCount];
            for (var i = 0; i < words.Length; i++)
            {
                var wordAddress = NormalizeAddress(address + (uint)(i * 2));
                if (IsCapturedHostTrapStub(wordAddress))
                {
                    words[i] = 0x4AFC;
                    continue;
                }

                var byteOffset = offset + (i * 2);
                var high = byteOffset < _snapshot.Bytes.Length ? _snapshot.Bytes[byteOffset] : 0;
                var low = byteOffset + 1 < _snapshot.Bytes.Length ? _snapshot.Bytes[byteOffset + 1] : 0;
                words[i] = (ushort)((high << 8) | low);
            }

            return true;
        }

        public bool TryGetGeneration(uint address, out uint generation)
        {
            address = NormalizeAddress(address);
            var page = address & ~(uint)(CodeGenerationPageSize - 1);
            var stamp = _snapshot.GenerationStamp;
            if (stamp.Pages == null || stamp.Generations == null)
            {
                generation = 0;
                return false;
            }

            for (var i = 0; i < stamp.Pages.Length && i < stamp.Generations.Length; i++)
            {
                if (stamp.Pages[i] == page)
                {
                    generation = stamp.Generations[i];
                    return true;
                }
            }

            generation = 0;
            return false;
        }

        public bool ContainsRange(uint address, int byteLength)
            => TryGetOffset(address, byteLength, out _);

        private bool TryGetOffset(uint address, int byteLength, out int offset)
        {
            offset = 0;
            if (_snapshot.Bytes == null || byteLength < 0)
            {
                return false;
            }

            address = NormalizeAddress(address);
            var root = NormalizeAddress(_snapshot.Root);
            var distance = NormalizeAddress(address - root);
            if (distance > int.MaxValue)
            {
                return false;
            }

            offset = (int)distance;
            return offset >= 0 && offset + byteLength <= _snapshot.Bytes.Length;
        }

        private bool IsCapturedHostTrapStub(uint address)
        {
            var trapStubs = _snapshot.HostTrapStubAddresses;
            if (trapStubs == null)
            {
                return false;
            }

            address = NormalizeAddress(address);
            for (var i = 0; i < trapStubs.Length; i++)
            {
                if (NormalizeAddress(trapStubs[i]) == address)
                {
                    return true;
                }
            }

            return false;
        }

        private static uint NormalizeAddress(uint address)
            => address & 0x00FF_FFFF;
    }
}
