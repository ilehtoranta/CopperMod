using CopperMod.Abstractions;

namespace CopperMod.Ahx;

/// <summary>AHX0/AHX1 (Abyss' Highest Experience) module loader.</summary>
public sealed class AhxFormat : IModuleFormat
{
    private readonly Func<byte[]> _loadPlayer;

    public AhxFormat()
        : this(AhxReferencePlayerBinary.LoadConfigured)
    {
    }

    /// <summary>Creates an AHX loader using an explicitly supplied local reference binary.</summary>
    public AhxFormat(ReadOnlyMemory<byte> referencePlayer)
        : this(() => AhxReferencePlayerBinary.Validate(referencePlayer.Span))
    {
    }

    internal AhxFormat(Func<byte[]> loadPlayer)
    {
        _loadPlayer = loadPlayer ?? throw new ArgumentNullException(nameof(loadPlayer));
    }

    public string Name => "AHX";
    public bool CanLoad(ReadOnlySpan<byte> data) => data.Length >= 14 && data[0] == (byte)'T' && data[1] == (byte)'H' && data[2] == (byte)'X' && data[3] <= 1;
    public IModuleSong Load(ReadOnlySpan<byte> data)
    {
        if (!CanLoad(data))
        {
            throw new UnsupportedModuleFormatException("The data is not an AHX0/AHX1 module.");
        }

        // Parse first so malformed modules fail without executing guest code.
        _ = AhxParser.Parse(data);
        try
        {
            return new Ahx68kSong(data, _loadPlayer());
        }
        catch (ModuleLoadException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ModuleLoadException(
                $"The AHX 68000 reference player failed while loading the module: {ex.Message}",
                ex);
        }
    }
}

