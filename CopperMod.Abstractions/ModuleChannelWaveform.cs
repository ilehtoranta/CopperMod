using System;
using System.Collections.Generic;

namespace CopperMod.Abstractions
{
    /// <summary>
    /// Per-tracker-channel PCM waveform data captured from one rendered interval.
    /// </summary>
    public sealed class ModuleChannelWaveform
    {
        /// <summary>
        /// Creates a channel waveform snapshot.
        /// </summary>
        public ModuleChannelWaveform(IReadOnlyList<ModuleChannelWaveformChannel> channels, int sourceFrameCount, int sampleRate)
        {
            Channels = channels ?? throw new ArgumentNullException(nameof(channels));
            if (sourceFrameCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceFrameCount));
            }

            if (sampleRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            }

            SourceFrameCount = sourceFrameCount;
            SampleRate = sampleRate;
        }

        /// <summary>
        /// Captured tracker channels.
        /// </summary>
        public IReadOnlyList<ModuleChannelWaveformChannel> Channels { get; }

        /// <summary>
        /// Number of PCM frames covered by this snapshot.
        /// </summary>
        public int SourceFrameCount { get; }

        /// <summary>
        /// Source sample rate in Hz.
        /// </summary>
        public int SampleRate { get; }
    }

    /// <summary>
    /// Mono PCM waveform data for one tracker channel.
    /// </summary>
    public sealed class ModuleChannelWaveformChannel
    {
        /// <summary>
        /// Creates a channel waveform.
        /// </summary>
        public ModuleChannelWaveformChannel(int channelIndex, float[] samples, bool isActive)
        {
            if (channelIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(channelIndex));
            }

            ChannelIndex = channelIndex;
            Samples = samples ?? throw new ArgumentNullException(nameof(samples));
            IsActive = isActive;
        }

        /// <summary>
        /// Tracker channel index.
        /// </summary>
        public int ChannelIndex { get; }

        /// <summary>
        /// Mono PCM samples for this tracker channel.
        /// </summary>
        public float[] Samples { get; }

        /// <summary>
        /// Whether the channel was active during this interval.
        /// </summary>
        public bool IsActive { get; }
    }
}
