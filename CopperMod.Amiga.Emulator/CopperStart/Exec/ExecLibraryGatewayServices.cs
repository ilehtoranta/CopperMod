using System;
using Copper68k;

namespace CopperMod.Amiga.CopperStart.Exec;

/// <summary>Routes Exec library/device/resource LVOs by active Exec mode.</summary>
internal sealed class ExecLibraryGatewayServices
{
    private readonly Func<bool> _usesRomExec;
    private readonly Action<M68kCpuState> _openRom;
    private readonly Action<M68kCpuState> _closeRom;
    private readonly Action<M68kCpuState> _addRomLibrary;
    private readonly Action<M68kCpuState> _removeRomLibrary;
    private readonly Action<M68kCpuState> _addRomDevice;
    private readonly Action<M68kCpuState> _removeRomDevice;
    private readonly Action<M68kCpuState> _openRomDevice;
    private readonly Action<M68kCpuState> _closeRomDevice;
    private readonly Action<M68kCpuState> _addRomResource;
    private readonly Action<M68kCpuState> _removeRomResource;
    private readonly Func<M68kCpuState, uint> _openRomResource;
    private readonly Action<M68kCpuState> _openCompatibilityLibrary;
    private readonly Action<M68kCpuState> _allocateCompatibilityLibrary;
    private readonly uint _compatibilityDosLibrary;

    public ExecLibraryGatewayServices(Func<bool> usesRomExec,
        Action<M68kCpuState> openRom, Action<M68kCpuState> closeRom,
        Action<M68kCpuState> addRomLibrary, Action<M68kCpuState> removeRomLibrary,
        Action<M68kCpuState> addRomDevice, Action<M68kCpuState> removeRomDevice,
        Action<M68kCpuState> openRomDevice, Action<M68kCpuState> closeRomDevice,
        Action<M68kCpuState> addRomResource,
        Action<M68kCpuState> removeRomResource, Func<M68kCpuState, uint> openRomResource,
        Action<M68kCpuState> openCompatibilityLibrary, Action<M68kCpuState> allocateCompatibilityLibrary,
        uint compatibilityDosLibrary)
    {
        _usesRomExec = usesRomExec; _openRom = openRom; _closeRom = closeRom; _addRomLibrary = addRomLibrary; _removeRomLibrary = removeRomLibrary;
        _addRomDevice = addRomDevice; _removeRomDevice = removeRomDevice; _openRomDevice = openRomDevice; _closeRomDevice = closeRomDevice;
        _addRomResource = addRomResource; _removeRomResource = removeRomResource; _openRomResource = openRomResource;
        _openCompatibilityLibrary = openCompatibilityLibrary; _allocateCompatibilityLibrary = allocateCompatibilityLibrary; _compatibilityDosLibrary = compatibilityDosLibrary;
    }
    public void OpenLibrary(M68kCpuState state) { if (_usesRomExec()) _openRom(state); else _openCompatibilityLibrary(state); }
    public void CloseLibrary(M68kCpuState state) { if (_usesRomExec()) _closeRom(state); else state.D[0] = 0; }
    public void AddLibrary(M68kCpuState state) { if (_usesRomExec()) _addRomLibrary(state); else _allocateCompatibilityLibrary(state); state.D[0] = 0; }
    public void RemLibrary(M68kCpuState state) { if (_usesRomExec()) _removeRomLibrary(state); state.D[0] = 0; }
    public void AddDevice(M68kCpuState state) { if (_usesRomExec()) _addRomDevice(state); state.D[0] = 0; }
    public void RemDevice(M68kCpuState state) { if (_usesRomExec()) _removeRomDevice(state); state.D[0] = 0; }
    public void OpenDevice(M68kCpuState state) { if (_usesRomExec()) _openRomDevice(state); else state.D[0] = 0xFFFF_FFFF; }
    public void CloseDevice(M68kCpuState state) { if (_usesRomExec()) _closeRomDevice(state); else state.D[0] = 0; }
    public void AddResource(M68kCpuState state) { if (_usesRomExec()) _addRomResource(state); state.D[0] = 0; }
    public void RemResource(M68kCpuState state) { if (_usesRomExec()) _removeRomResource(state); state.D[0] = 0; }
    public void OpenResource(M68kCpuState state) => state.D[0] = _usesRomExec() ? _openRomResource(state) : 0;
    public void InitResident(M68kCpuState state) => state.D[0] = _compatibilityDosLibrary;
}
