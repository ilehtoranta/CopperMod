using System;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CopperStart.Exec;

namespace CopperMod.Amiga.CopperStart;

/// <summary>
/// Owns CopperStart's installation-mode lifetime.  It deliberately does not
/// decide when a ROM is ready; boot orchestration supplies that verified base.
/// </summary>
internal sealed class CopperStartRuntime : IDisposable
{
    private readonly CopperStartExecInstaller _execInstaller;
    private ExecServices? _activeRomExec;

    public CopperStartRuntime(AmigaBus bus, Func<uint, ExecServices> createExecServices)
        => _execInstaller = new CopperStartExecInstaller(bus, createExecServices);

    public void InstallSyntheticExec(uint syntheticExecBase)
        => _execInstaller.InstallCopperStart(syntheticExecBase);

    public void ActivateRomExec(uint nativeExecBase)
    {
        _activeRomExec?.Dispose();
        _activeRomExec = _execInstaller.InstallKickstartRomOverlay(nativeExecBase);
    }

    public void Reset()
    {
        _activeRomExec?.Dispose();
        _activeRomExec = null;
        _execInstaller.Reset();
    }

    public void Dispose() => Reset();
}
