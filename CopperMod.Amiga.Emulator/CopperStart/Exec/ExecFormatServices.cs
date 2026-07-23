using System;
using System.Collections.Generic;
using System.Text;
using Copper68k;
using CopperMod.Amiga.Bus;

namespace CopperMod.Amiga.CopperStart.Exec;

/// <summary>RawDoFmt output sequencing, including guest putch continuations.</summary>
internal sealed class ExecFormatServices
{
    private readonly AmigaBus _bus;
    private readonly uint _continuationAddress;
    private readonly Func<uint, int, string> _readString;
    private readonly Queue<byte> _characters = new();
    private readonly StringBuilder _nativeOutput = new();
    private uint _putCh;

    public ExecFormatServices(AmigaBus bus, uint continuationAddress, Func<uint, int, string> readString)
    { _bus = bus ?? throw new ArgumentNullException(nameof(bus)); _continuationAddress = continuationAddress; _readString = readString ?? throw new ArgumentNullException(nameof(readString)); }

    public uint RawDoFmt(M68kCpuState state)
    {
        var format = state.A[0]; var data = state.A[1]; _putCh = state.A[2];
        if (format == 0) return data;
        _characters.Clear();
        foreach (var character in RawDoFmtFormatter.Format(_bus, format, data, address => _readString(address, 1024), out data)) _characters.Enqueue((byte)character);
        EmitNext(state);
        return data;
    }
    public void Continue(M68kCpuState state) => EmitNext(state);
    private void EmitNext(M68kCpuState state)
    {
        if (_characters.Count == 0) { _putCh = 0; return; }
        var character = _characters.Dequeue();
        if (_putCh == 0) { _nativeOutput.Append((char)character); EmitNext(state); return; }
        state.D[0] = character; state.A[7] -= 4; _bus.WriteLong(state.A[7], _continuationAddress, state.Cycles); state.ProgramCounter = _putCh + 6;
        if (_bus.TryInvokeHostGatewayAt(_putCh, state))
        {
            if (state.ProgramCounter == _putCh + 6) { state.ProgramCounter = _bus.ReadLong(state.A[7]); state.A[7] += 4; EmitNext(state); }
            return;
        }
        state.ProgramCounter = _putCh;
    }
}
