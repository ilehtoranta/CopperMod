using CopperMod.Abstractions;
using CopperMod.Cust;

namespace CopperMod.EaglePlayer;

/// <summary>Loads TFMX songs through authenticated Amiga EaglePlayer replayers.</summary>
public sealed class TfmxFormat : IModuleFormatWithContext
{
    /// <inheritdoc />
    public string Name => "TFMX";

    /// <inheritdoc />
    public bool CanLoad(ReadOnlySpan<byte> data) => data.Length >= 0x200 &&
        (data.StartsWith("TFMX-SONG"u8) || data.StartsWith("TFMX "u8));

    /// <inheritdoc />
    public bool CanLoad(ModuleLoadContext context) => CanLoad(context.DataSpan);

    /// <inheritdoc />
    public IModuleSong Load(ReadOnlySpan<byte> data) => Load(new ModuleLoadContext(data.ToArray()));

    /// <inheritdoc />
    public IModuleSong Load(ModuleLoadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!CanLoad(context)) throw new UnsupportedModuleFormatException("The data is not a TFMX song.");
        // Each player's guest Check2 routine decides whether it supports this variant.
        foreach (var id in new[] { "tfmx-pro", "tfmx-7v", "tfmx-1-5" })
        {
            EaglePlayerSession session;
            try
            {
                session = EaglePlayerSession.LoadConfigured(EaglePlayerReplayerCatalog.Get(id), context.DataSpan, context);
            }
            catch (UnsupportedModuleFormatException) { continue; }

            using (session)
            {
                var title = context.SourcePath == null ? "TFMX song" : Path.GetFileName(context.SourcePath);
                return new CustSong(session.Machine, new ModuleMetadata(
                    title: title, formatName: Name,
                    formatVersion: EaglePlayerReplayerCatalog.Get(id).DisplayName,
                    channelCount: 4,
                    tags: new Dictionary<string, string> { ["Machine"] = "Amiga 500 PAL", ["Kickstart"] = session.Machine.KickstartDescription }));
            }
        }
        throw new UnsupportedModuleFormatException("No supported TFMX replayer accepted this song.");
    }
}
