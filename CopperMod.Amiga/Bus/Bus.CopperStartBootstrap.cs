using System;
using System.Buffers.Binary;

namespace CopperMod.Amiga.Bus;

internal sealed partial class Bus
{
    // Private guest opt-in extension. No ROM bytes, vectors or hardware registers
    // are overlaid; ordinary Kickstart and the existing OS gateways are unchanged.
    internal const uint CopperStartBootstrapToken = 0x43534731;
    private bool _copperStartBootstrapEnabled = true;
    public bool CopperStartBootstrapEnabled
    {
        get => _copperStartBootstrapEnabled;
        set
        {
            _copperStartBootstrapEnabled = value;
            ConfigureCopperStartBootstrap();
        }
    }
    public int CopperStartBootstrapCalls { get; private set; }
    public bool CopperStartBootstrapReady { get; private set; }

    private void ConfigureCopperStartBootstrap()
    {
        _relocatableHostTrapStubs.Remove(CopperStartBootstrapToken);
        if (_copperStartBootstrapEnabled)
            _relocatableHostTrapStubs[CopperStartBootstrapToken] = HandleCopperStartBootstrap;
        ResetCopperStartBootstrapSession();
    }

    private void ResetCopperStartBootstrapSession()
    { CopperStartBootstrapCalls = 0; CopperStartBootstrapReady = false; }

    private M68kHostGatewayResult HandleCopperStartBootstrap(M68kCpuState state)
    {
        CopperStartBootstrapCalls++; CopperStartBootstrapReady = false;
        var request = state.D[0]; state.D[0] = 0;
        const uint receipt = 0x760, magic = 0x43534231, reply = 0x43504D31, version = 1;
        var stack = state.A[7];
        if (state.LastInstructionProgramCounter != 0x780 || state.ProgramCounter != 0x786 ||
            (state.StatusRegister & M68kCpuState.Supervisor) == 0 || request != magic ||
            state.D[1] != version || state.A[0] != receipt || (stack & 1) != 0 ||
            ChipRam.Length < 0x80000 || stack < 0x2000 || stack > (uint)ChipRam.Length-4)
            return M68kHostGatewayResult.Completed;
        var caller = BinaryPrimitives.ReadUInt32BigEndian(ChipRam.AsSpan((int)stack, 4));
        var r = ChipRam.AsSpan((int)receipt, 32);
        if (caller < 0xF80000 || caller >= 0x1000000 || (caller & 1) != 0 ||
            BinaryPrimitives.ReadUInt32BigEndian(r) != magic ||
            BinaryPrimitives.ReadUInt32BigEndian(r[4..]) != version ||
            BinaryPrimitives.ReadUInt32BigEndian(r[12..]) != 1)
            return M68kHostGatewayResult.Completed;
        var challenge = BinaryPrimitives.ReadUInt32BigEndian(r[8..]);
        WriteHostLong(receipt+16, reply);
        WriteHostLong(receipt+20, version);
        WriteHostLong(receipt+24, challenge ^ reply ^ version);
        WriteHostLong(receipt+28, 1); // native-ROM bootstrap capability
        state.D[0] = reply; CopperStartBootstrapReady = true;
        return M68kHostGatewayResult.Completed;
    }
}
