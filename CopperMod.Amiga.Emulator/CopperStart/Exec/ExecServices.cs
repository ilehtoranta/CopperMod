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
        // Task, signal, and trap LVOs mutate only the active ROM ExecBase and
        // hand every actual context transition to the untouched KS Schedule/
        // Switch vectors.  This keeps task frames ROM-owned while allowing the
        // host implementations to share one wait/wakeup state machine.
        -282, -288, -294, -300, -306, -312, -318, -324, -330, -336, -342, -348,
        -354, -360, -366,
        -372, -378, -390, -522, -534, -558, -564, -570, -576, -624, -630, -678, -684, -690, -696, -702, -708, -714, -720
    ];
    private static readonly int[] DedicatedLvos =
    [ -456, -462, -468, -474, -480, -102, -96, -90, -84, -78, -276, -198, -204, -210, -216, -396, -402, -408, -414, -432, -438, -444, -450, -486, -492, -498, -552 ];

    private readonly AmigaBus _bus;
    private readonly List<HostLibraryGateway> _gateways;
    private readonly Func<M68kCpuState, M68kHostGatewayResult> _reschedule;
    private readonly List<(uint Address, uint Token)> _privateGateways = new();
    private HostLibraryGatewayRegistry _registry;

    public ExecServices(AmigaBus bus, uint execBase, Func<M68kCpuState, int, M68kHostGatewayResult> generic,
        Func<M68kCpuState, M68kHostGatewayResult> doIo, Func<M68kCpuState, M68kHostGatewayResult> sendIo, Func<M68kCpuState, M68kHostGatewayResult> checkIo, Func<M68kCpuState, M68kHostGatewayResult> waitIo, Func<M68kCpuState, M68kHostGatewayResult> abortIo, Action<M68kCpuState> findResident, Action<M68kCpuState> ok,
        Action<M68kCpuState> findName, Action<M68kCpuState> allocMem, Action<M68kCpuState> allocMemAndStore,
        Action<M68kCpuState> allocAbs, Action<M68kCpuState> freeMem, Action<M68kCpuState> availMem,
        Action<M68kCpuState> openLibrary, Action<M68kCpuState> closeLibrary,
        Action<M68kCpuState> addLibrary, Action<M68kCpuState> remLibrary,
        Action<M68kCpuState> addDevice, Action<M68kCpuState> remDevice,
        Action<M68kCpuState> openDevice, Action<M68kCpuState> closeDevice,
        Action<M68kCpuState> addResource,
        Action<M68kCpuState> remResource, Action<M68kCpuState> openResource,
        Action<M68kCpuState> makeFunctions, Action<M68kCpuState> makeLibrary,
        Action<M68kCpuState> initResident, Func<M68kCpuState, M68kHostGatewayResult> reschedule)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _reschedule = reschedule ?? throw new ArgumentNullException(nameof(reschedule));
        ExecBase = execBase;
        _registry = new HostLibraryGatewayRegistry(_bus);
        var dedicated = new HashSet<int>(DedicatedLvos);
        _gateways = new List<HostLibraryGateway>();
        for (var lvo = -6; lvo >= -1200; lvo -= 6)
            if (!dedicated.Contains(lvo)) { var captured = lvo; _gateways.Add(new(captured, state => generic(state, captured))); }
        _gateways.AddRange([new(-456, doIo), new(-462, sendIo), new(-468, checkIo), new(-474, waitIo), new(-480, abortIo), new(-102, initResident), new(-96, findResident), new(-90, makeFunctions), new(-84, makeLibrary), new(-78, state => generic(state, -78)),
            new(-276, findName), new(-198, allocMem), new(-204, allocAbs), new(-210, freeMem), new(-396, addLibrary), new(-402, remLibrary),
            new(-216, availMem), new(-408, openLibrary), new(-414, closeLibrary), new(-432, addDevice), new(-438, remDevice),
            new(-444, openDevice), new(-450, closeDevice), new(-486, addResource),
            new(-492, remResource), new(-498, openResource), new(-552, openLibrary)]);
        _registry.AddLibrary(execBase, _gateways);
    }

    public uint ExecBase { get; }
    public bool IsInstalled => _registry.IsInstalled;
    public void InstallCopperStart()
    {
        _registry.InstallSynthetic();
        InstallPrivateGateways();
    }
    public void InstallKickstartRomOverlay()
    {
        if (IsInstalled) return;
        Dispose();
        _registry = new HostLibraryGatewayRegistry(_bus);
        _registry.AddLibrary(ExecBase, _gateways.FindAll(gateway =>
            // Device I/O is owned by the opened device.  The CopperStart
            // compatibility shim has a small synchronous boot-disk path,
            // but installing that as a ROM Exec DoIO/SendIO replacement
            // would redirect every request (input, timer, trackdisk, ...)
            // away from its device BeginIO vector.  Keep the KS 3.1 I/O LVOs
            // native until a concrete replacement device registers its own
            // BeginIO/AbortIO gateways.
            gateway.Lvo is not (-456 or -462 or -468 or -474 or -480) &&
            // InitResident has one ROM vector for every resident type. Keep it
            // native until resources and the remaining resident classes have
            // the same complete lifecycle as AUTOINIT libraries/devices.
            gateway.Lvo is -96 or -90 or -84 or -78 or -276 or -198 or -204 or -210 or -216 or -396 or -402 or -408 or -414 or -432 or -438 or -444 or -450 or -486 or -492 or -498 or -552 ||
            Array.IndexOf(ConcreteGenericLvos, gateway.Lvo) >= 0));
        _registry.InstallRomOverlays();
        InstallPrivateGateways();
    }
    public void Dispose()
    {
        for (var i = _privateGateways.Count - 1; i >= 0; i--) _bus.RemoveHostGateway(_privateGateways[i].Address, _privateGateways[i].Token);
        _privateGateways.Clear();
        _registry.Dispose();
    }
    private void InstallPrivateGateways()
    {
        if (_privateGateways.Count != 0) return;
        _privateGateways.Add((unchecked((uint)((int)ExecBase + ExecLvos.PrivateWait)), _bus.RegisterHostGateway(unchecked((uint)((int)ExecBase + ExecLvos.PrivateWait)), state => _gateways.Find(g => g.Lvo == -318).Handler(state))));
        _privateGateways.Add((unchecked((uint)((int)ExecBase + ExecLvos.PrivateReschedule)), _bus.RegisterHostGateway(unchecked((uint)((int)ExecBase + ExecLvos.PrivateReschedule)), _reschedule)));
    }
}
