using CopperMod.Abstractions;
using CopperMod.Cust;
using CopperMod.Replayers;

namespace CopperMod.EaglePlayer;

/// <summary>
/// Runs an authenticated EaglePlayer/DeliTracker 68k player Hunk against a separate module image.
/// </summary>
public sealed class EaglePlayerSession : IDisposable
{
    private readonly CustMachine _machine;
    private bool _disposed;

    private EaglePlayerSession(byte[] authenticatedPlayer, byte[] module, ModuleLoadContext? loadContext)
    {
        if (!HunkParser.Identify(authenticatedPlayer))
        {
            throw new UnsupportedModuleFormatException("The configured EaglePlayer replay binary is not an Amiga Hunk file.");
        }

        var hunk = HunkParser.Parse(authenticatedPlayer);
        if (!DeliTagParser.TryFindTags(hunk, out var tags))
        {
            throw new UnsupportedModuleFormatException("The configured EaglePlayer replay binary does not expose supported DeliTracker player tags.");
        }

        _machine = new CustMachine(hunk, tags, loadContext, module);
    }

    /// <summary>Loads and authenticates the configured player before starting its guest code.</summary>
    public static EaglePlayerSession LoadConfigured(
        BinaryReplayerDescriptor descriptor,
        ReadOnlySpan<byte> module,
        ModuleLoadContext? loadContext = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (module.IsEmpty)
        {
            throw new ModuleLoadException("An EaglePlayer module image cannot be empty.");
        }

        return new EaglePlayerSession(BinaryReplayerLoader.LoadConfigured(descriptor), module.ToArray(), loadContext);
    }

    /// <summary>Loads a catalog player, acquiring its authenticated archive on first use.</summary>
    public static EaglePlayerSession LoadConfigured(
        EaglePlayerReplayerRegistration registration,
        ReadOnlySpan<byte> module,
        ModuleLoadContext? loadContext = null)
    {
        ArgumentNullException.ThrowIfNull(registration);
        if (registration.Kind != EaglePlayerAssetKind.Player)
        {
            throw new ArgumentException("An EaglePlayer companion engine cannot be started as a player plugin.", nameof(registration));
        }

        if (module.IsEmpty)
        {
            throw new ModuleLoadException("An EaglePlayer module image cannot be empty.");
        }

        return new EaglePlayerSession(registration.LoadConfigured(), module.ToArray(), loadContext);
    }

    /// <summary>Authenticates explicitly supplied player bytes before starting their guest code.</summary>
    public static EaglePlayerSession Create(
        BinaryReplayerDescriptor descriptor,
        ReadOnlySpan<byte> player,
        ReadOnlySpan<byte> module,
        ModuleLoadContext? loadContext = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (module.IsEmpty)
        {
            throw new ModuleLoadException("An EaglePlayer module image cannot be empty.");
        }

        var authenticatedPlayer = BinaryReplayerLoader.Validate(descriptor, player);
        return new EaglePlayerSession(authenticatedPlayer, module.ToArray(), loadContext);
    }

    /// <summary>Diagnostics emitted by bounded guest execution and host callbacks.</summary>
    public IReadOnlyList<ModuleDiagnostic> Diagnostics => _machine.Diagnostics;

    /// <summary>Number of subsongs reported by the player.</summary>
    public int SubSongCount => _machine.SubSongCount;

    /// <summary>Currently selected zero-based subsong.</summary>
    public int CurrentSubSongIndex => _machine.CurrentSubSongIndex;

    /// <summary>Whether the player called the DeliTracker song-end callback.</summary>
    public bool SongEnded => _machine.SongEnded;

    /// <summary>Current emulated CPU cycle.</summary>
    public long Cycle => _machine.Cpu.State.Cycles;

    /// <summary>Current player interrupt/render quantum in A500 CPU cycles.</summary>
    public long QuantumCycleCount => _machine.QuantumCycleCount;

    /// <summary>Returns the host frame count corresponding to the current guest quantum.</summary>
    public int GetCurrentTickFrameCount(int sampleRate)
    {
        ThrowIfDisposed();
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        return Math.Clamp(
            (int)Math.Round(QuantumCycleCount / CustConstants.A500PalCpuClockHz * sampleRate),
            1,
            CustConstants.MaxRenderFramesPerTick);
    }

    /// <summary>Renders one guest interrupt quantum through cycle-timed Paula.</summary>
    public void RenderQuantum(Span<float> destination, int frames, int channels, int sampleRate, bool captureChannels = false)
    {
        ThrowIfDisposed();
        _machine.RenderQuantum(destination, frames, channels, sampleRate, captureChannels);
    }

    /// <summary>Resets guest hardware and replay state while retaining the current subsong.</summary>
    public void Reset()
    {
        ThrowIfDisposed();
        _machine.Reset(_machine.CurrentSubSongIndex);
    }

    /// <summary>Selects and initializes a zero-based subsong.</summary>
    public void SelectSubSong(int index)
    {
        ThrowIfDisposed();
        _machine.SelectSubSong(index);
    }

    /// <summary>Explicitly invokes the player's bounded stop and shutdown callbacks.</summary>
    public void End()
    {
        ThrowIfDisposed();
        _machine.End();
    }

    /// <summary>Releases the host session without running untrusted guest shutdown callbacks.</summary>
    public void Dispose()
    {
        _disposed = true;
    }

    internal CustMachine Machine => _machine;

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
