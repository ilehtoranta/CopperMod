/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;

namespace CopperFloat;

/// <summary>Classifies a raw extended 80-bit floating-point encoding.</summary>
public enum ExtF80Class
{
    /// <summary>A positive or negative zero.</summary>
    Zero,
    /// <summary>A finite value with a zero exponent and no explicit integer bit.</summary>
    Subnormal,
    /// <summary>A finite normalized value.</summary>
    Normal,
    /// <summary>A positive or negative infinity.</summary>
    Infinity,
    /// <summary>A quiet not-a-number value.</summary>
    QuietNaN,
    /// <summary>A signaling not-a-number value.</summary>
    SignalingNaN,
    /// <summary>A noncanonical encoding that is unsupported by extF80 arithmetic.</summary>
    Unsupported
}

/// <summary>Specifies how a floating-point result is rounded.</summary>
public enum ExtF80RoundingMode
{
    /// <summary>Round to the nearest value, selecting an even low bit on a tie.</summary>
    ToNearestEven,
    /// <summary>Round toward zero.</summary>
    TowardZero,
    /// <summary>Round toward negative infinity.</summary>
    TowardNegativeInfinity,
    /// <summary>Round toward positive infinity.</summary>
    TowardPositiveInfinity
}

/// <summary>Specifies the significand precision used for an extended result.</summary>
public enum ExtF80Precision
{
    /// <summary>Round the significand to 24 bits.</summary>
    Single = 24,
    /// <summary>Round the significand to 53 bits.</summary>
    Double = 53,
    /// <summary>Round the significand to the full 64-bit extF80 precision.</summary>
    Extended = 64
}

/// <summary>Specifies when tininess is detected for underflow reporting.</summary>
public enum ExtF80TininessMode
{
    /// <summary>Detect tininess before rounding.</summary>
    BeforeRounding,
    /// <summary>Detect tininess after rounding.</summary>
    AfterRounding
}

/// <summary>IEEE floating-point exception flags produced by an operation.</summary>
[Flags]
public enum FloatingPointExceptionFlags
{
    /// <summary>No exception condition occurred.</summary>
    None = 0,
    /// <summary>The exact result was not representable.</summary>
    Inexact = 1 << 0,
    /// <summary>A tiny inexact result occurred.</summary>
    Underflow = 1 << 1,
    /// <summary>The rounded result exceeded the finite exponent range.</summary>
    Overflow = 1 << 2,
    /// <summary>A finite nonzero value was divided by zero.</summary>
    DivideByZero = 1 << 3,
    /// <summary>The operation or operand encoding was invalid.</summary>
    Invalid = 1 << 4
}

/// <summary>The ordering result of an extended floating-point comparison.</summary>
public enum ExtF80Comparison
{
    /// <summary>The left operand is less than the right operand.</summary>
    Less,
    /// <summary>The operands compare equal.</summary>
    Equal,
    /// <summary>The left operand is greater than the right operand.</summary>
    Greater,
    /// <summary>At least one operand is a not-a-number value.</summary>
    Unordered
}

/// <summary>Controls rounding and underflow behavior for an operation.</summary>
public readonly record struct ExtF80Context(
    ExtF80RoundingMode RoundingMode,
    ExtF80Precision Precision,
    ExtF80TininessMode TininessMode)
{
    /// <summary>The IEEE default context: nearest-even, extended precision, tininess after rounding.</summary>
    public static ExtF80Context Default { get; } = new(
        ExtF80RoundingMode.ToNearestEven,
        ExtF80Precision.Extended,
        ExtF80TininessMode.AfterRounding);
}

/// <summary>Contains a floating-point result and the exception flags raised while producing it.</summary>
public readonly record struct FloatingPointResult<T>(T Value, FloatingPointExceptionFlags Flags)
    where T : struct;
