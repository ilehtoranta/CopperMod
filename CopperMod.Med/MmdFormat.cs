using System;
using CopperMod.Abstractions;

namespace CopperMod.Med
{
    /// <summary>
    /// MED/OctaMED MMD0-MMD3 module format loader.
    /// </summary>
    public sealed class MmdFormat : IModuleFormat
    {
        /// <inheritdoc />
        public string Name => "MED/OctaMED MMD";

        /// <inheritdoc />
        public bool CanLoad(ReadOnlySpan<byte> data)
        {
            if (data.Length < MmdConstants.HeaderLength)
            {
                return false;
            }

            var id = BigEndian.ReadUInt32(data, 0, "MMD id");
            if (!IsSupportedId(id))
            {
                return false;
            }

            var length = BigEndian.ReadUInt32(data, 4, "module length");
            return length >= MmdConstants.HeaderLength && length <= data.Length;
        }

        /// <inheritdoc />
        public IModuleSong Load(ReadOnlySpan<byte> data)
        {
            if (!CanLoad(data))
            {
                throw new UnsupportedModuleFormatException("The data is not a supported MMD0-MMD3 module.");
            }

            var copy = data.ToArray();
            var module = MmdParser.Parse(copy);
            return new MmdSong(module);
        }

        internal static bool IsSupportedId(uint id)
        {
            return id == MmdConstants.Mmd0
                || id == MmdConstants.Mmd1
                || id == MmdConstants.Mmd2
                || id == MmdConstants.Mmd3;
        }
    }
}
