using System;
using CopperMod.Abstractions;

namespace CopperMod.Cust
{
    /// <summary>
    /// Amiga CUST custom module loader.
    /// </summary>
    public sealed class CustFormat : IModuleFormatWithContext
    {
        /// <inheritdoc />
        public string Name => "Amiga CUST";

        /// <inheritdoc />
        public bool CanLoad(ReadOnlySpan<byte> data)
        {
            return CanLoad(new ModuleLoadContext(data.ToArray()));
        }

        /// <inheritdoc />
        public bool CanLoad(ModuleLoadContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (!HunkParser.Identify(context.DataSpan))
            {
                return false;
            }

            try
            {
                var hunk = HunkParser.Parse(context.DataSpan);
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
            return Load(new ModuleLoadContext(data.ToArray()));
        }

        /// <inheritdoc />
        public IModuleSong Load(ModuleLoadContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (!HunkParser.Identify(context.DataSpan))
            {
                throw new UnsupportedModuleFormatException("The data is not an Amiga Hunk CUST module.");
            }

            var copy = context.DataSpan.ToArray();
            var hunk = HunkParser.Parse(copy);
            if (!DeliTagParser.TryFindTags(hunk, out var tags))
            {
                throw new UnsupportedModuleFormatException("The Hunk file does not expose supported DeliTracker CUST tags.");
            }

            return new CustSong(hunk, tags, context);
        }
    }
}
