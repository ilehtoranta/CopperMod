using System;

namespace AmigaTracker.Abstractions
{
    /// <summary>
    /// Describes the audio format requested from a module renderer.
    /// </summary>
    public sealed class AudioRenderOptions
    {
        /// <summary>
        /// The default output sample rate.
        /// </summary>
        public const int DefaultSampleRate = 44100;

        /// <summary>
        /// The default number of interleaved output channels.
        /// </summary>
        public const int DefaultChannelCount = 2;

        /// <summary>
        /// Default stereo, 44.1 kHz, Paula-style sample hold render options.
        /// </summary>
        public static readonly AudioRenderOptions Default = new AudioRenderOptions();

        /// <summary>
        /// Creates audio render options.
        /// </summary>
        public AudioRenderOptions(
            int sampleRate = DefaultSampleRate,
            int channelCount = DefaultChannelCount,
            bool interpolationEnabled = false)
        {
            if (sampleRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");
            }

            if (channelCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(channelCount), channelCount, "Channel count must be positive.");
            }

            SampleRate = sampleRate;
            ChannelCount = channelCount;
            InterpolationEnabled = interpolationEnabled;
        }

        /// <summary>
        /// Output sample rate in Hz.
        /// </summary>
        public int SampleRate { get; }

        /// <summary>
        /// Number of interleaved PCM channels to write per audio frame.
        /// </summary>
        public int ChannelCount { get; }

        /// <summary>
        /// Whether the renderer should interpolate sample playback when it can.
        /// </summary>
        public bool InterpolationEnabled { get; }

        /// <summary>
        /// Converts an audio frame count to an interleaved sample count.
        /// </summary>
        public int GetSampleCount(int frameCount)
        {
            if (frameCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(frameCount), frameCount, "Frame count cannot be negative.");
            }

            return checked(frameCount * ChannelCount);
        }
    }
}
