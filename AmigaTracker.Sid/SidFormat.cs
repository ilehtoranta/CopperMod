using System;
using AmigaTracker.Abstractions;

namespace AmigaTracker.Sid
{
    /// <summary>
    /// PSID/RSID SID music loader.
    /// </summary>
    public sealed class SidFormat : IModuleFormat
    {
        /// <inheritdoc />
        public string Name => "C64 SID";

        /// <inheritdoc />
        public bool CanLoad(ReadOnlySpan<byte> data)
        {
            return SidParser.Identify(data);
        }

        /// <inheritdoc />
        public IModuleSong Load(ReadOnlySpan<byte> data)
        {
            if (!CanLoad(data))
            {
                throw new UnsupportedModuleFormatException("The data is not a supported PSID/RSID SID file.");
            }

            var copy = data.ToArray();
            var module = SidParser.Parse(copy);
            return new SidSong(module);
        }
    }
}
