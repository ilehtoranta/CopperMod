using System;
using System.Collections.Generic;
using Copper68k;
using CopperMod.Amiga.Bus;

namespace CopperMod.Amiga.CopperStart;

/// <summary>Flattens implemented library LVO contributions into host gateways.</summary>
internal sealed class HostLibraryGatewayRegistry : IDisposable
{
    private readonly AmigaBus _bus;
    private readonly Dictionary<uint, Action<M68kCpuState>> _handlers = new();
    private readonly List<HostGatewayOverlay> _overlays = new();
    public HostLibraryGatewayRegistry(AmigaBus bus) => _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    public bool IsInstalled { get; private set; }
    public void AddLibrary(uint libraryBase, IEnumerable<HostLibraryGateway> gateways)
    {
        if (IsInstalled) throw new InvalidOperationException("Gateways must be contributed before installation.");
        foreach (var gateway in gateways)
            if (!_handlers.TryAdd(unchecked((uint)((int)libraryBase + gateway.Lvo)), gateway.Handler))
                throw new InvalidOperationException("A host gateway is already registered at this LVO.");
    }
    public void InstallSynthetic() => Install(false);
    public void InstallRomOverlays() => Install(true);
    private void Install(bool overlays)
    {
        if (IsInstalled) return;
        foreach (var entry in _handlers)
            if (overlays) _overlays.Add(_bus.InstallHostGatewayOverlay(entry.Key, entry.Value));
            else _bus.RegisterHostGateway(entry.Key, entry.Value);
        IsInstalled = true;
    }
    public void Dispose() { for (var i = _overlays.Count - 1; i >= 0; i--) _overlays[i].Dispose(); _overlays.Clear(); IsInstalled = false; }
}

internal readonly record struct HostLibraryGateway(int Lvo, Action<M68kCpuState> Handler);
