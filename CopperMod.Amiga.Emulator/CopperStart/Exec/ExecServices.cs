using System;
using System.Collections.Generic;
using Copper68k;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CopperStart;

namespace CopperMod.Amiga.CopperStart.Exec;

/// <summary>Exec LVO contribution owned by CopperStart's host-library layer.</summary>
internal sealed class ExecServices : IDisposable
{
    // ROM takeover is deliberately more conservative than CopperStart's
    // synthetic compatibility table.  These functions operate directly on
    // the mature ROM-created MemList and do not alter boot-time task or
    // library lifecycle state.  Every other vector remains native until its
    // real-ROM startup and lifecycle contract has its own smoke coverage.
    private static readonly int[] RomSafeOverlayLvos = [ -276, -198, -204, -210, -216 ];
    private static readonly int[] DedicatedLvos =
    [ -456, -462, -468, -474, -480, -102, -96, -90, -84, -78, -276, -198, -204, -210, -216, -396, -402, -408, -414, -432, -438, -444, -450, -486, -492, -498, -552 ];

    private readonly AmigaBus _bus;
    private readonly List<HostLibraryGateway> _gateways;
    private readonly Func<M68kCpuState, M68kHostGatewayResult> _reschedule;
    private readonly Func<M68kCpuState, int, M68kHostGatewayResult> _generic;
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
        _generic = generic ?? throw new ArgumentNullException(nameof(generic));
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
            Array.IndexOf(RomSafeOverlayLvos, gateway.Lvo) >= 0));
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
        RegisterPrivate(ExecLvos.AllocMemAligned);
        RegisterPrivate(ExecLvos.AllocateAligned);
        RegisterPrivate(ExecLvos.AllocVecAligned);
        RegisterPrivate(ExecLvos.AllocPooledAligned);
        RegisterPrivate(ExecLvos.AllocVecPooled);
        RegisterPrivate(ExecLvos.FreeVecPooled);
        RegisterPrivate(ExecLvos.FindExecNode);
        RegisterPrivate(ExecLvos.AddExecNodeA);
        RegisterPrivate(ExecLvos.AddResident);
        RegisterPrivate(ExecLvos.AvailPool);
        RegisterPrivate(ExecLvos.PutMsgHead);
    }
    private void RegisterPrivate(int lvo)
    {
        var address = unchecked((uint)((int)ExecBase + lvo));
        _privateGateways.Add((address, _bus.RegisterHostGateway(address, state => _generic(state, lvo))));
    }
}
