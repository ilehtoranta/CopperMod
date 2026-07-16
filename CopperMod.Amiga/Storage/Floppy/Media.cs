/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CopperMod.Amiga
{
    internal static class AmigaDiskGeometry
    {
        public const int SectorSize = 512;
        public const int SectorsPerTrack = 11;
        public const int HeadCount = 2;
        public const int CylinderCount = 80;
        public const int TrackCount = CylinderCount * HeadCount;
        public const int StandardAdfSize = SectorSize * SectorsPerTrack * HeadCount * CylinderCount;
    }

    internal interface IAmigaDiskImage
    {
        byte[] Data { get; }

        string Name { get; }

        bool HasCompleteSectorData { get; }

        bool HasPreservedTrackData { get; }

        bool DefaultWriteProtected { get; }

        bool IsDirty { get; }

        bool CanWriteTracks { get; }

        ReadOnlySpan<byte> BootBlock { get; }

        ReadOnlySpan<byte> ReadSector(int cylinder, int head, int sector);

        ReadOnlySpan<byte> ReadSector(int logicalSector);

        ReadOnlySpan<byte> ReadBytes(int byteOffset, int byteCount);

        AmigaEncodedTrack ReadEncodedTrack(int cylinder, int head);

        bool TryWriteEncodedTrack(int cylinder, int head, AmigaEncodedTrack track);
    }

    internal readonly struct AmigaEncodedTrack
    {
        public AmigaEncodedTrack(ReadOnlyMemory<byte> encodedData, int bitLength)
            : this(encodedData, bitLength, startBit: 0, AmigaTrackFeatures.None)
        {
        }

        public AmigaEncodedTrack(
            ReadOnlyMemory<byte> encodedData,
            int bitLength,
            int startBit,
            AmigaTrackFeatures features = AmigaTrackFeatures.None)
            : this(encodedData, bitLength, startBit, features, regions: null)
        {
        }

        public AmigaEncodedTrack(
            ReadOnlyMemory<byte> encodedData,
            int bitLength,
            int startBit,
            AmigaTrackFeatures features,
            IReadOnlyList<AmigaTrackRegion>? regions)
        {
            if (encodedData.IsEmpty)
            {
                throw new ArgumentException("Encoded track data must not be empty.", nameof(encodedData));
            }

            if (bitLength <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bitLength), bitLength, "Encoded track bit length must be positive.");
            }

            if (bitLength > checked(encodedData.Length * 8))
            {
                throw new ArgumentOutOfRangeException(nameof(bitLength), bitLength, "Encoded track bit length cannot exceed the backing data.");
            }

            if (startBit < 0 || startBit >= bitLength)
            {
                throw new ArgumentOutOfRangeException(nameof(startBit), startBit, "Encoded track start bit must be inside the track.");
            }

            EncodedData = encodedData;
            BitLength = bitLength;
            StartBit = startBit;
            Features = features;
            Regions = NormalizeRegions(regions, bitLength);
        }

        public ReadOnlyMemory<byte> EncodedData { get; }

        public int BitLength { get; }

        public int StartBit { get; }

        public AmigaTrackFeatures Features { get; }

        public IReadOnlyList<AmigaTrackRegion> Regions { get; }

        public int ByteLength => (BitLength + 7) / 8;

        public ReadOnlySpan<byte> EncodedSpan => EncodedData.Span;

        public static AmigaEncodedTrack FromBytes(byte[] ownedData)
        {
            ArgumentNullException.ThrowIfNull(ownedData);
            return new AmigaEncodedTrack(ownedData, checked(ownedData.Length * 8));
        }

        public byte ReadByteAtBit(int bitOffset)
        {
            return (byte)ReadBits(bitOffset, 8);
        }

        public ushort ReadUInt16AtBit(int bitOffset)
        {
            return (ushort)ReadBits(bitOffset, 16);
        }

        public uint ReadUInt32AtBit(int bitOffset)
        {
            return (uint)ReadBits(bitOffset, 32);
        }

        public static int WrapBitOffset(int value, int divisor)
        {
            if (divisor <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(divisor));
            }

            return WrapBitOffsetUnchecked(value, divisor);
        }

        private static int WrapBitOffsetUnchecked(int value, int divisor)
        {
            Debug.Assert(divisor > 0);

            var result = value % divisor;
            return result < 0 ? result + divisor : result;
        }

        private ulong ReadBits(int bitOffset, int bitCount)
        {
            Debug.Assert(bitCount is >= 0 and <= 64);

            if (BitLength <= 0)
            {
                throw new InvalidOperationException("Cannot read from an empty encoded track.");
            }

            var span = EncodedData.Span;
            bitOffset = WrapBitOffsetUnchecked(bitOffset, BitLength);
            var endBit = bitOffset + bitCount;
            if (bitCount == 0)
            {
                return 0;
            }

            if (endBit <= BitLength)
            {
                var byteOffset = bitOffset >> 3;
                var bitShift = bitOffset & 7;
                var byteCount = (bitShift + bitCount + 7) >> 3;
                if (byteCount <= 8)
                {
                    var window = 0ul;
                    for (var index = 0; index < byteCount; index++)
                    {
                        window = (window << 8) | span[byteOffset + index];
                    }

                    var shift = (byteCount * 8) - bitShift - bitCount;
                    window >>= shift;
                    return bitCount == 64 ? window : window & ((1ul << bitCount) - 1);
                }
            }

            var value = 0ul;
            for (var bit = 0; bit < bitCount; bit++)
            {
                var trackBit = (bitOffset + bit) % BitLength;
                var dataBit = (span[trackBit >> 3] >> (7 - (trackBit & 7))) & 1;
                value = (value << 1) | (uint)dataBit;
            }

            return value;
        }

        private static IReadOnlyList<AmigaTrackRegion> NormalizeRegions(IReadOnlyList<AmigaTrackRegion>? regions, int trackBitLength)
        {
            if (regions == null || regions.Count == 0)
            {
                return [];
            }

            var normalized = regions.ToArray();
            for (var index = 0; index < normalized.Length; index++)
            {
                var region = normalized[index];
                if (region.StartBit >= trackBitLength ||
                    region.BitLength > trackBitLength - region.StartBit)
                {
                    throw new ArgumentOutOfRangeException(nameof(regions), "Track regions must be fully inside the encoded track.");
                }
            }

            return Array.AsReadOnly(normalized);
        }
    }

    [Flags]
    internal enum AmigaTrackFeatures
    {
        None = 0,
        PreservedTrackData = 1 << 0,
        WeakData = 1 << 1,
        ApproximateWeakData = 1 << 2,
        FluxCapture = 1 << 3,
        ApproximateIndex = 1 << 4,
        NoFlux = 1 << 5
    }

    internal readonly struct AmigaTrackRegion
    {
        public AmigaTrackRegion(int startBit, int bitLength, AmigaTrackFeatures features)
        {
            if (startBit < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(startBit));
            }

            if (bitLength <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bitLength));
            }

            StartBit = startBit;
            BitLength = bitLength;
            Features = features;
        }

        public int StartBit { get; }

        public int BitLength { get; }

        public AmigaTrackFeatures Features { get; }
    }
}
