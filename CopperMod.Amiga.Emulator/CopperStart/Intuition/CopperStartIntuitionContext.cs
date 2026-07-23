using System;
using Copper68k;

namespace CopperMod.Amiga.CopperStart.Intuition;

/// <summary>Concrete bridge for Intuition's synthetic CopperStart compatibility session.</summary>
internal sealed class CopperStartIntuitionContext
{
    public CopperStartIntuitionContext(
        Action<int> logCall, Action<uint> configureScreen, Action<uint> configureWindow,
        Func<uint> ensureScreen, Func<uint> ensureWindow, Func<uint> ensureView, Func<uint> ensureHostObject,
        Func<uint> getViewAddress, Func<uint> getViewPortAddress, Action<uint> selectFrontViewPort,
        Action<M68kCpuState> addGList, Action<M68kCpuState> modifyIdcmp, Action<M68kCpuState> setWindowTitles,
        Func<long, uint> rethinkDisplay, Action<M68kCpuState> allocRemember)
    {
        LogCall = logCall; ConfigureScreen = configureScreen; ConfigureWindow = configureWindow;
        EnsureScreen = ensureScreen; EnsureWindow = ensureWindow; EnsureView = ensureView; EnsureHostObject = ensureHostObject;
        GetViewAddress = getViewAddress; GetViewPortAddress = getViewPortAddress; SelectFrontViewPort = selectFrontViewPort;
        AddGList = addGList; ModifyIdcmp = modifyIdcmp; SetWindowTitles = setWindowTitles; RethinkDisplay = rethinkDisplay; AllocRemember = allocRemember;
    }
    public Action<int> LogCall { get; } public Action<uint> ConfigureScreen { get; } public Action<uint> ConfigureWindow { get; }
    public Func<uint> EnsureScreen { get; } public Func<uint> EnsureWindow { get; } public Func<uint> EnsureView { get; } public Func<uint> EnsureHostObject { get; }
    public Func<uint> GetViewAddress { get; } public Func<uint> GetViewPortAddress { get; } public Action<uint> SelectFrontViewPort { get; }
    public Action<M68kCpuState> AddGList { get; } public Action<M68kCpuState> ModifyIdcmp { get; } public Action<M68kCpuState> SetWindowTitles { get; }
    public Func<long, uint> RethinkDisplay { get; } public Action<M68kCpuState> AllocRemember { get; }
}
