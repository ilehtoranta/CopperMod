using System;
using System.Collections.Generic;
using Copper68k;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CopperStart;

namespace CopperMod.Amiga.CopperStart.Exec;

/// <summary>Exec LVO contribution owned by CopperStart's host-library layer.</summary>
internal sealed class ExecServices : IDisposable
{
    private static readonly int[] ConcreteGenericLvos =
    [
        -120, -126, -132, -138, -150, -156, -168, -174, -186, -192, -222, -228, -234, -240, -246, -252, -258, -264, -270,
        -282, -288, -294, -300, -306, -318, -324, -330, -336, -342, -348, -354, -360, -366,
        -372, -378, -384, -390, -522, -534, -624, -630, -684, -690, -696, -702, -708, -714
    ];
    private static readonly int[] DedicatedLvos =
    [ -456, -102, -96, -276, -198, -204, -210, -216, -396, -402, -408, -414, -432, -486, -492, -498, -552 ];

    private readonly AmigaBus _bus;
    private readonly List<HostLibraryGateway> _gateways;
    private HostLibraryGatewayRegistry _registry;

    public ExecServices(AmigaBus bus, uint execBase, Action<M68kCpuState, int> generic,
        Action<M68kCpuState> doIo, Action<M68kCpuState> findResident, Action<M68kCpuState> ok,
        Action<M68kCpuState> findName, Action<M68kCpuState> allocMem, Action<M68kCpuState> allocMemAndStore,
        Action<M68kCpuState> allocAbs, Action<M68kCpuState> freeMem, Action<M68kCpuState> availMem,
        Action<M68kCpuState> openLibrary, Action<M68kCpuState> closeLibrary,
        Action<M68kCpuState> addLibrary, Action<M68kCpuState> remLibrary,
        Action<M68kCpuState> addDevice, Action<M68kCpuState> addResource,
        Action<M68kCpuState> remResource, Action<M68kCpuState> openResource,
        Action<M68kCpuState> initResident)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        ExecBase = execBase;
        _registry = new HostLibraryGatewayRegistry(_bus);
        var dedicated = new HashSet<int>(DedicatedLvos);
        _gateways = new List<HostLibraryGateway>();
        for (var lvo = -6; lvo >= -1200; lvo -= 6)
            if (!dedicated.Contains(lvo)) { var captured = lvo; _gateways.Add(new(captured, state => generic(state, captured))); }
        _gateways.AddRange([new(-456, doIo), new(-102, initResident), new(-96, findResident),
            new(-276, findName), new(-198, allocMem), new(-204, allocAbs), new(-210, freeMem), new(-396, addLibrary), new(-402, remLibrary),
            new(-216, availMem), new(-408, openLibrary), new(-414, closeLibrary), new(-432, addDevice), new(-486, addResource),
            new(-492, remResource), new(-498, openResource), new(-552, openLibrary)]);
        _registry.AddLibrary(execBase, _gateways);
    }

    public uint ExecBase { get; }
    public bool IsInstalled => _registry.IsInstalled;
    public void InstallCopperStart() => _registry.InstallSynthetic();
    public void InstallKickstartRomOverlay()
    {
        if (IsInstalled) return;
        Dispose();
        _registry = new HostLibraryGatewayRegistry(_bus);
        _registry.AddLibrary(ExecBase, _gateways.FindAll(gateway =>
            // InitResident remains native in ROM mode until its AUTOINIT path can
            // use the real MakeLibrary/InitStruct implementation.
            gateway.Lvo is -456 or -96 or -276 or -198 or -204 or -210 or -216 or -396 or -402 or -408 or -414 or -432 or -486 or -492 or -498 or -552 ||
            Array.IndexOf(ConcreteGenericLvos, gateway.Lvo) >= 0));
        _registry.InstallRomOverlays();
    }
    public void Dispose() => _registry.Dispose();
}
