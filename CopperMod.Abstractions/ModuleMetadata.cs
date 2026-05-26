using System;
using System.Collections.Generic;

namespace CopperMod.Abstractions
{
    /// <summary>
    /// Describes a loaded module song without tying it to a specific tracker format.
    /// </summary>
    public sealed class ModuleMetadata
    {
        private static readonly IReadOnlyDictionary<string, string> NoTags = new Dictionary<string, string>();

        /// <summary>
        /// Empty metadata for loaders that have not parsed metadata yet.
        /// </summary>
        public static readonly ModuleMetadata Empty = new ModuleMetadata();

        /// <summary>
        /// Creates module metadata.
        /// </summary>
        public ModuleMetadata(
            string? title = null,
            string? formatName = null,
            string? formatVersion = null,
            int channelCount = 0,
            int instrumentCount = 0,
            int sampleCount = 0,
            int? initialSpeed = null,
            double? initialTempo = null,
            IReadOnlyDictionary<string, string>? tags = null)
        {
            ThrowIfNegative(channelCount, nameof(channelCount));
            ThrowIfNegative(instrumentCount, nameof(instrumentCount));
            ThrowIfNegative(sampleCount, nameof(sampleCount));

            if (initialSpeed < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialSpeed), initialSpeed, "Initial speed cannot be negative.");
            }

            if (initialTempo < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialTempo), initialTempo, "Initial tempo cannot be negative.");
            }

            Title = title;
            FormatName = formatName;
            FormatVersion = formatVersion;
            ChannelCount = channelCount;
            InstrumentCount = instrumentCount;
            SampleCount = sampleCount;
            InitialSpeed = initialSpeed;
            InitialTempo = initialTempo;
            Tags = tags is null ? NoTags : new Dictionary<string, string>(tags, StringComparer.Ordinal);
        }

        /// <summary>
        /// Song title, when present.
        /// </summary>
        public string? Title { get; }

        /// <summary>
        /// Module format family, such as MED.
        /// </summary>
        public string? FormatName { get; }

        /// <summary>
        /// Module format version or variant, such as MMD0.
        /// </summary>
        public string? FormatVersion { get; }

        /// <summary>
        /// Number of tracker channels used by the song.
        /// </summary>
        public int ChannelCount { get; }

        /// <summary>
        /// Number of instruments, when known.
        /// </summary>
        public int InstrumentCount { get; }

        /// <summary>
        /// Number of samples, when known.
        /// </summary>
        public int SampleCount { get; }

        /// <summary>
        /// Initial tracker speed, usually ticks per row.
        /// </summary>
        public int? InitialSpeed { get; }

        /// <summary>
        /// Initial tempo, when the source format exposes one.
        /// </summary>
        public double? InitialTempo { get; }

        /// <summary>
        /// Additional format-specific metadata.
        /// </summary>
        public IReadOnlyDictionary<string, string> Tags { get; }

        private static void ThrowIfNegative(int value, string parameterName)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(parameterName, value, "Value cannot be negative.");
            }
        }
    }
}
