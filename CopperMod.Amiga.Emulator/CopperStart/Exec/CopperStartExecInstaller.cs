using System;
using CopperMod.Amiga.Bus;

namespace CopperMod.Amiga.CopperStart.Exec;

/// <summary>
/// Installs one shared Exec contribution set through either CopperStart's
/// synthetic bases or the discovered native Kickstart ExecBase.
/// </summary>
internal sealed class CopperStartExecInstaller
{
    private readonly AmigaBus _bus;
    private readonly Func<uint, ExecServices> _createServices;
    private ExecServices? _syntheticServices;

    public CopperStartExecInstaller(AmigaBus bus, Func<uint, ExecServices> createServices)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _createServices = createServices ?? throw new ArgumentNullException(nameof(createServices));
    }

    public void InstallCopperStart(uint syntheticExecBase)
    {
        _syntheticServices?.Dispose();
        _syntheticServices = _createServices(syntheticExecBase);
        _syntheticServices.InstallCopperStart();
    }

    public ExecServices InstallKickstartRomOverlay(uint nativeExecBase)
    {
        var services = _createServices(nativeExecBase);
        services.InstallKickstartRomOverlay();
        return services;
    }

    public AmigaBus Bus => _bus;
    public void Reset()
    {
        _syntheticServices?.Dispose();
        _syntheticServices = null;
    }
}
