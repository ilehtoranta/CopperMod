/* Copyright (C) 2026 Ilkka Lehtoranta; SPDX-License-Identifier: MIT */
namespace Copper68k;

/// <summary>Optional diagnostics that distinguish compiled execution from interpreter fallback.</summary>
public interface IM68kJitDiagnostics
{
    /// <summary>Returns counters without enabling instruction tracing.</summary>
    M68kJitStatistics GetJitStatistics();
}

/// <summary>A snapshot of JIT execution counters.</summary>
/// <param name="CompiledTraces">Traces compiled successfully.</param>
/// <param name="CompiledInstructions">Instructions executed through generated code.</param>
/// <param name="FallbackInstructions">Instructions executed through the interpreter.</param>
/// <param name="Invalidations">Compiled trace invalidations.</param>
public readonly record struct M68kJitStatistics(long CompiledTraces, long CompiledInstructions, long FallbackInstructions, long Invalidations);
