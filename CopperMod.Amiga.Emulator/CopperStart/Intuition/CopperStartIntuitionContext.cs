using System;
using System.Collections.Generic;
using Copper68k;

namespace CopperMod.Amiga.CopperStart.Intuition;

/// <summary>Concrete bridge for Intuition's synthetic CopperStart compatibility session.</summary>
internal sealed class CopperStartIntuitionContext
{
    // Optional graphics resources are owned by their successfully staged
    // Screens. This is host-side bookkeeping only: guest Screen/View links
    // remain authoritative for the actual graphics-library lifecycle.
    private readonly HashSet<uint> _preparedScreenResources = new();
    public CopperStartIntuitionContext(
        Action<int> logCall, Action<uint> configureScreen, Action<uint> configureWindow,
        Action<M68kCpuState> closeScreen,
        Func<uint, uint, bool> configureScreenTags,
        Func<uint, bool> completeScreenOpen,
        Func<uint> ensureScreen, Func<uint> ensureWindow, Func<uint> ensureView, Func<uint> ensureHostObject,
        Func<uint> openWindow, Action<uint> closeWindow,
        Func<uint> getViewAddress, Func<uint> getViewPortAddress, Action<uint> selectFrontViewPort,
        Action<M68kCpuState> addGList, Action<M68kCpuState> modifyIdcmp, Action<M68kCpuState> setWindowTitles,
        Action<M68kCpuState> showTitle, Action<M68kCpuState> getScreenData,
        Action<M68kCpuState> queryOverscan,
        Func<long, uint> rethinkDisplay, Action<M68kCpuState> allocRemember,
        Func<uint, long, bool>? activateScreen = null,
        Func<uint, M68kCpuState, bool>? activateScreenState = null,
        Action<bool>? finalizeScreenOpen = null,
        Func<uint, uint>? getWindowViewPortAddress = null,
        Func<uint, bool>? validateNewScreenAddress = null,
        Func<uint, bool>? validateNewWindowAddress = null,
        Func<uint, bool>? validateScreenAddress = null,
        Func<uint, bool>? validateWindowAddress = null,
        Func<M68kCpuState, bool>? tryModifyIdcmp = null,
        Func<uint, uint>? nextObject = null,
        Func<uint, bool>? prepareScreenResources = null,
        Action<uint, bool>? finalizeScreenResources = null,
        Func<M68kCpuState, uint>? rethinkDisplayWithState = null)
    {
        LogCall = logCall; ConfigureScreen = configureScreen; ConfigureWindow = configureWindow;
        CloseScreen = closeScreen;
        ConfigureScreenTags = configureScreenTags;
        CompleteScreenOpen = completeScreenOpen;
        EnsureScreen = ensureScreen; EnsureWindow = ensureWindow; EnsureView = ensureView; EnsureHostObject = ensureHostObject;
        OpenWindow = openWindow; CloseWindow = closeWindow;
        GetViewAddress = getViewAddress; GetViewPortAddress = getViewPortAddress; SelectFrontViewPort = selectFrontViewPort;
        GetWindowViewPortAddress = getWindowViewPortAddress;
        AddGList = addGList; ModifyIdcmp = modifyIdcmp; SetWindowTitles = setWindowTitles; ShowTitle = showTitle; GetScreenData = getScreenData; QueryOverscan = queryOverscan; RethinkDisplay = rethinkDisplay; AllocRemember = allocRemember;
        ActivateScreen = activateScreen ?? ((_, _) => false);
        ActivateScreenState = activateScreenState;
        FinalizeScreenOpen = finalizeScreenOpen;
        ValidateNewScreenAddress = validateNewScreenAddress;
        ValidateNewWindowAddress = validateNewWindowAddress;
        ValidateScreenAddress = validateScreenAddress;
        ValidateWindowAddress = validateWindowAddress;
        TryModifyIdcmp = tryModifyIdcmp;
        NextObject = nextObject;
        PrepareScreenResources = prepareScreenResources;
        FinalizeScreenResources = finalizeScreenResources;
        RethinkDisplayWithState = rethinkDisplayWithState;
    }
    public Action<int> LogCall { get; } public Action<uint> ConfigureScreen { get; } public Action<uint> ConfigureWindow { get; }
    public Action<M68kCpuState> CloseScreen { get; }
    public Func<uint, uint, bool> ConfigureScreenTags { get; }
    public Func<uint, bool> CompleteScreenOpen { get; }
    public Func<uint> EnsureScreen { get; } public Func<uint> EnsureWindow { get; } public Func<uint> EnsureView { get; } public Func<uint> EnsureHostObject { get; }
    public Func<uint> OpenWindow { get; } public Action<uint> CloseWindow { get; }
    public Func<uint> GetViewAddress { get; } public Func<uint> GetViewPortAddress { get; } public Action<uint> SelectFrontViewPort { get; }
    /// <summary>
    /// Optional Window*-aware ViewPortAddress owner. A native-shaped host can
    /// provide this callback so the window's own WScreen link determines the
    /// returned ViewPort; compatibility hosts may retain the single-screen
    /// callback above.
    /// </summary>
    public Func<uint, uint>? GetWindowViewPortAddress { get; }
    public Action<M68kCpuState> AddGList { get; } public Action<M68kCpuState> ModifyIdcmp { get; } public Action<M68kCpuState> SetWindowTitles { get; } public Action<M68kCpuState> ShowTitle { get; } public Action<M68kCpuState> GetScreenData { get; } public Action<M68kCpuState> QueryOverscan { get; }
    /// <summary>
    /// Optional status-aware ModifyIDCMP owner. The legacy Action callback is
    /// retained for providers that cannot report a decline; compatibility
    /// hosts use this seam when a public Window field is readable but not
    /// writable.
    /// </summary>
    public Func<M68kCpuState, bool>? TryModifyIdcmp { get; }
    /// <summary>
    /// Optional host-memory BOOPSI cursor bridge.  The portable Intuition
    /// vector owns the ABI; a CopperStart host supplies only the guest-memory
    /// implementation so native/provider ownership remains explicit.
    /// </summary>
    public Func<uint, uint>? NextObject { get; }
    public Func<long, uint> RethinkDisplay { get; } public Action<M68kCpuState> AllocRemember { get; }
    /// <summary>
    /// Optional state-aware host boundary. The classic vector has no explicit
    /// View argument, but a host shim may use the captured frame to recover a
    /// staged compatibility View before the first publication.
    /// </summary>
    public Func<M68kCpuState, uint>? RethinkDisplayWithState { get; }
    public Func<uint, long, bool> ActivateScreen { get; }

    /// <summary>
    /// Optional final lifecycle boundary for a synthetic OpenScreen request.
    /// The compatibility builder may stage and validate a Screen before the
    /// display owner reconstructs its View. Hosts that publish SA_ErrorCode
    /// use this callback to commit success only after that final handoff, or
    /// to publish a failure code after the newly-created Screen is rolled
    /// back.
    /// </summary>
    public Action<bool>? FinalizeScreenOpen { get; }

    /// <summary>
    /// Optional NewScreen envelope validator.  Synthetic owners use this to
    /// reject a non-null pointer whose later legacy/extension fields would
    /// wrap the 32-bit guest address space before any host callback reads it.
    /// Provider/native hosts may leave the seam unset and retain ownership of
    /// their own high-address mapping rules.
    /// </summary>
    public Func<uint, bool>? ValidateNewScreenAddress { get; }

    /// <summary>
    /// Optional NewWindow envelope validator.  Synthetic owners use this to
    /// reject a non-null pointer whose last public field would wrap the
    /// 32-bit guest address space before any host callback reads it.
    /// Provider/native hosts may leave the seam unset and retain ownership of
    /// their own high-address mapping rules.
    /// </summary>
    public Func<uint, bool>? ValidateNewWindowAddress { get; }

    /// <summary>
    /// Optional Screen envelope validator for synthetic teardown and query
    /// entry points.  Hosts that own a native/provider Screen mapping may
    /// leave the seam unset and retain their own address-envelope policy.
    /// </summary>
    public Func<uint, bool>? ValidateScreenAddress { get; }

    /// <summary>
    /// Optional Window envelope validator for synthetic teardown and query
    /// entry points.  Hosts that own a native/provider Window mapping may
    /// leave the seam unset and retain their own address-envelope policy.
    /// </summary>
    public Func<uint, bool>? ValidateWindowAddress { get; }

    /// <summary>
    /// Optional graphics-side screen-resource owner.  Intuition calls this
    /// after a Screen envelope has been staged but before display rethink;
    /// the callback may publish an owned View/ViewPort/RasInfo/BitMap chain or
    /// decline so native/provider ownership remains available.
    /// </summary>
    public Func<uint, bool>? PrepareScreenResources { get; }

    /// <summary>
    /// Completes or rolls back the optional graphics-side resource handoff.
    /// A false result retires a prepared chain; foreign screens must be
    /// ignored by the callback rather than claimed by the compatibility host.
    /// </summary>
    public Action<uint, bool>? FinalizeScreenResources { get; }

    internal bool TryPrepareScreenResources(uint screen, out bool prepared)
    {
        prepared = false;
        if (PrepareScreenResources is null)
            return true;

        // The synthetic host may legitimately rediscover the same live
        // Screen while an earlier open handoff still owns its graphics-side
        // View/ViewPort/RasInfo chain.  That is not a second resource
        // transfer: rerunning the provider and finalizing it again could
        // duplicate palette/bitmap ownership or turn one later CloseScreen
        // into two retirements.  Report successful admission, but leave the
        // request unprepared so the caller does not issue a second commit.
        if (_preparedScreenResources.Contains(screen))
            return true;

        if (!PrepareScreenResources(screen))
        {
            // A provider may have made provisional host allocations before it
            // declines the handoff. Its false finalization remains the
            // request-local cleanup hook, but never becomes a committed owner.
            FinalizeScreenResources?.Invoke(screen, false);
            return false;
        }

        _preparedScreenResources.Add(screen);
        prepared = true;
        return true;
    }

    internal void FinalizePreparedScreenResources(uint screen, bool succeeded)
    {
        if (screen == 0 || !_preparedScreenResources.Contains(screen))
            return;

        FinalizeScreenResources?.Invoke(screen, succeeded);
        if (!succeeded)
            _preparedScreenResources.Remove(screen);
    }

    /// <summary>
    /// Optional state-aware ScreenToFront boundary.  The legacy callback only
    /// receives a cycle value, so it cannot return scheduler advances to the
    /// caller's live CPU frame.  Hosts that can preserve the full frame use
    /// this callback; older hosts continue through <see cref="ActivateScreen" />.
    /// </summary>
    public Func<uint, M68kCpuState, bool>? ActivateScreenState { get; }
}
