using System;
using AmigaTracker.Abstractions;

namespace AmigaTracker.ProTracker
{
    /// <summary>
    /// ProTracker and closely related Amiga MOD format loader.
    /// </summary>
    public sealed class ProTrackerFormat : IModuleFormat
    {
        /// <inheritdoc />
        public string Name => "ProTracker MOD";

        /// <inheritdoc />
        public bool CanLoad(ReadOnlySpan<byte> data)
        {
            return ProTrackerParser.Identify(data).Recognized;
        }

        /// <inheritdoc />
        public IModuleSong Load(ReadOnlySpan<byte> data)
        {
            var identity = ProTrackerParser.Identify(data);
            if (!identity.Recognized)
            {
                throw new UnsupportedModuleFormatException("The data is not a supported ProTracker MOD module.");
            }

            if (identity.IsPacked)
            {
                throw new UnsupportedModuleFormatException("Packed ProTracker modules are detected but not supported.");
            }

            var copy = data.ToArray();
            var module = ProTrackerParser.Parse(copy, identity);
            return new ProTrackerSong(module);
        }
    }
}
