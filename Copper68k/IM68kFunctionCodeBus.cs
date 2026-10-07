/* Copyright (C) 2026 Ilkka Lehtoranta; SPDX-License-Identifier: MIT */
namespace Copper68k;

/// <summary>Optional bus capability for the alternate address spaces used by MOVES.</summary>
public interface IM68kFunctionCodeBus
{
    /// <summary>
    /// Gets or sets the function code for a MOVES operand access. Null selects
    /// the ordinary instruction/data and supervisor/user function code.
    /// </summary>
    byte? AlternateFunctionCode { get; set; }
}
