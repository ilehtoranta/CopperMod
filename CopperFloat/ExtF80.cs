/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Buffers.Binary;

namespace CopperFloat;

/// <summary>Stores the complete raw encoding of an extended 80-bit floating-point value.</summary>
public readonly struct ExtF80 : IEquatable<ExtF80>
{
    /// <summary>The number of bytes in the external extended 80-bit encoding.</summary>
    public const int EncodedSize = 10;

    /// <summary>The exponent bias used by the format.</summary>
    public const int ExponentBias = 16383;

    internal const ulong IntegerBit = 0x8000_0000_0000_0000;
    internal const ulong QuietBit = 0x4000_0000_0000_0000;
    internal const ulong FractionMask = 0x7fff_ffff_ffff_ffff;

    /// <summary>Creates a value from its raw sign/exponent and significand fields.</summary>
    private ExtF80(ushort signExponent, ulong significand)
    {
        SignExponent = signExponent;
        Significand = significand;
    }

    /// <summary>Gets the raw sign and biased-exponent field.</summary>
    public ushort SignExponent { get; }

    /// <summary>Gets the raw explicit-integer-bit significand.</summary>
    public ulong Significand { get; }

    /// <summary>Gets whether the sign bit is set.</summary>
    public bool Sign => (SignExponent & 0x8000) != 0;

    /// <summary>Gets the raw 15-bit exponent.</summary>
    public int BiasedExponent => SignExponent & 0x7fff;

    /// <summary>Gets the encoding classification.</summary>
    public ExtF80Class Classification
    {
        get
        {
            var exponent = BiasedExponent;
            var integerBit = (Significand & IntegerBit) != 0;
            if (exponent == 0)
            {
                if (Significand == 0)
                {
                    return ExtF80Class.Zero;
                }

                return integerBit ? ExtF80Class.Unsupported : ExtF80Class.Subnormal;
            }

            if (exponent == 0x7fff)
            {
                if (!integerBit)
                {
                    return ExtF80Class.Unsupported;
                }

                if ((Significand & FractionMask) == 0)
                {
                    return ExtF80Class.Infinity;
                }

                return (Significand & QuietBit) != 0
                    ? ExtF80Class.QuietNaN
                    : ExtF80Class.SignalingNaN;
            }

            return integerBit ? ExtF80Class.Normal : ExtF80Class.Unsupported;
        }
    }

    /// <summary>Gets whether this is a canonical encoding.</summary>
    public bool IsCanonical => Classification != ExtF80Class.Unsupported;

    /// <summary>Positive zero.</summary>
    public static ExtF80 PositiveZero { get; } = FromBits(0, 0);

    /// <summary>Negative zero.</summary>
    public static ExtF80 NegativeZero { get; } = FromBits(0x8000, 0);

    /// <summary>Positive infinity.</summary>
    public static ExtF80 PositiveInfinity { get; } = FromBits(0x7fff, IntegerBit);

    /// <summary>Negative infinity.</summary>
    public static ExtF80 NegativeInfinity { get; } = FromBits(0xffff, IntegerBit);

    /// <summary>The canonical quiet NaN used for invalid operations without a NaN operand.</summary>
    public static ExtF80 QuietNaN { get; } = FromBits(0x7fff, IntegerBit | QuietBit);

    /// <summary>Creates a value without normalizing or validating its raw fields.</summary>
    public static ExtF80 FromBits(ushort signExponent, ulong significand)
        => new(signExponent, significand);

    /// <summary>Reads a 10-byte big-endian encoding.</summary>
    public static ExtF80 ReadBigEndian(ReadOnlySpan<byte> source)
    {
        if (source.Length < EncodedSize)
        {
            throw new ArgumentException("An extended 80-bit value requires 10 bytes.", nameof(source));
        }

        return FromBits(
            BinaryPrimitives.ReadUInt16BigEndian(source),
            BinaryPrimitives.ReadUInt64BigEndian(source[2..]));
    }

    /// <summary>Reads a 10-byte little-endian encoding.</summary>
    public static ExtF80 ReadLittleEndian(ReadOnlySpan<byte> source)
    {
        if (source.Length < EncodedSize)
        {
            throw new ArgumentException("An extended 80-bit value requires 10 bytes.", nameof(source));
        }

        return FromBits(
            BinaryPrimitives.ReadUInt16LittleEndian(source[8..]),
            BinaryPrimitives.ReadUInt64LittleEndian(source));
    }

    /// <summary>Writes this value as a 10-byte big-endian encoding.</summary>
    public void WriteBigEndian(Span<byte> destination)
    {
        if (destination.Length < EncodedSize)
        {
            throw new ArgumentException("An extended 80-bit value requires 10 bytes.", nameof(destination));
        }

        BinaryPrimitives.WriteUInt16BigEndian(destination, SignExponent);
        BinaryPrimitives.WriteUInt64BigEndian(destination[2..], Significand);
    }

    /// <summary>Writes this value as a 10-byte little-endian encoding.</summary>
    public void WriteLittleEndian(Span<byte> destination)
    {
        if (destination.Length < EncodedSize)
        {
            throw new ArgumentException("An extended 80-bit value requires 10 bytes.", nameof(destination));
        }

        BinaryPrimitives.WriteUInt64LittleEndian(destination, Significand);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[8..], SignExponent);
    }

    /// <inheritdoc />
    public bool Equals(ExtF80 other)
        => SignExponent == other.SignExponent && Significand == other.Significand;

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => obj is ExtF80 other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(SignExponent, Significand);

    /// <inheritdoc />
    public override string ToString()
        => $"0x{SignExponent:X4}:0x{Significand:X16}";

    /// <summary>Determines whether two values have identical raw encodings.</summary>
    public static bool operator ==(ExtF80 left, ExtF80 right) => left.Equals(right);

    /// <summary>Determines whether two values have different raw encodings.</summary>
    public static bool operator !=(ExtF80 left, ExtF80 right) => !left.Equals(right);
}
