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

internal static class M68kFunctionCodeAccess
{
    internal static uint Transfer(IM68kBus bus, uint address, M68kOperandSize size,
        byte fc, bool write, uint value, ref long cycle)
    {
        var selector = bus as IM68kFunctionCodeBus;
        var previous = selector?.AlternateFunctionCode;
        if (selector is not null) selector.AlternateFunctionCode = fc;
        try
        {
            if (!write)
                return size switch
                {
                    M68kOperandSize.Byte => bus.ReadByte(address, ref cycle, M68kBusAccessKind.CpuDataRead),
                    M68kOperandSize.Word => bus.ReadWord(address, ref cycle, M68kBusAccessKind.CpuDataRead),
                    _ => bus.ReadLong(address, ref cycle, M68kBusAccessKind.CpuDataRead)
                };
            switch (size)
            {
                case M68kOperandSize.Byte: bus.WriteByte(address, (byte)value, ref cycle, M68kBusAccessKind.CpuDataWrite); break;
                case M68kOperandSize.Word: bus.WriteWord(address, (ushort)value, ref cycle, M68kBusAccessKind.CpuDataWrite); break;
                default: bus.WriteLong(address, value, ref cycle, M68kBusAccessKind.CpuDataWrite); break;
            }
            return 0;
        }
        finally { if (selector is not null) selector.AlternateFunctionCode = previous; }
    }
}
