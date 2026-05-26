using System;
using CopperMod.Abstractions;

namespace CopperMod.Cust
{
    /// <summary>
    /// Amiga CUST custom module loader.
    /// </summary>
    public sealed class CustFormat : IModuleFormat
    {
        /// <inheritdoc />
        public string Name => "Amiga CUST";

        /// <inheritdoc />
        public bool CanLoad(ReadOnlySpan<byte> data)
        {
            if (!HunkParser.Identify(data))
            {
                return false;
            }

            try
            {
                var hunk = HunkParser.Parse(data);
                return DeliTagParser.TryFindTags(hunk, out _);
            }
            catch (ModuleLoadException)
            {
                return false;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        /// <inheritdoc />
        public IModuleSong Load(ReadOnlySpan<byte> data)
        {
            if (!HunkParser.Identify(data))
            {
                throw new UnsupportedModuleFormatException("The data is not an Amiga Hunk CUST module.");
            }

            var copy = data.ToArray();
            var hunk = HunkParser.Parse(copy);
            if (!DeliTagParser.TryFindTags(hunk, out var tags))
            {
                throw new UnsupportedModuleFormatException("The Hunk file does not expose supported DeliTracker CUST tags.");
            }

            return new CustSong(hunk, tags);
        }
    }
}
