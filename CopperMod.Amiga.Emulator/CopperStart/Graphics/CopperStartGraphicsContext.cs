using System;
using Copper68k;
using CopperMod.Amiga.Bus;

namespace CopperMod.Amiga.CopperStart.Graphics;

/// <summary>
/// Reset-scoped concrete bridge for CopperStart graphics services.
///
/// Ordinary graphics structures (View, ViewPort, RastPort and BitMap) may use
/// <see cref="Memory"/>.  Any operation that reaches the custom chips or changes
/// display scheduling must use its explicit callback instead, retaining normal
/// bus side effects and ordering.
/// </summary>
internal sealed class CopperStartGraphicsContext
{
    public CopperStartGraphicsContext(
        HostGuestMemory memory,
        Action<M68kCpuState> waitTof,
        Action<uint, ushort, long> writeCustomRegister,
        Action<uint> selectFrontViewPort,
        Action requestDisplayRebuild,
        Action<uint> initializeCompatibilityViewPort)
    {
        Memory = memory ?? throw new ArgumentNullException(nameof(memory));
        WaitTof = waitTof ?? throw new ArgumentNullException(nameof(waitTof));
        WriteCustomRegister = writeCustomRegister ?? throw new ArgumentNullException(nameof(writeCustomRegister));
        SelectFrontViewPort = selectFrontViewPort ?? throw new ArgumentNullException(nameof(selectFrontViewPort));
        RequestDisplayRebuild = requestDisplayRebuild ?? throw new ArgumentNullException(nameof(requestDisplayRebuild));
        InitializeCompatibilityViewPort = initializeCompatibilityViewPort ?? throw new ArgumentNullException(nameof(initializeCompatibilityViewPort));
    }

    public HostGuestMemory Memory { get; }
    public Action<M68kCpuState> WaitTof { get; }
    public Action<uint, ushort, long> WriteCustomRegister { get; }
    public Action<uint> SelectFrontViewPort { get; }
    public Action RequestDisplayRebuild { get; }
    public Action<uint> InitializeCompatibilityViewPort { get; }
}
