/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections.Generic;

namespace CopperMod.Amiga.Diagnostics
{
    internal sealed class AmigaChannelWaveform
    {
        public AmigaChannelWaveform(AmigaChannelWaveformChannel[] channels, int frameCount, int sampleRate)
        {
            Channels = channels ?? throw new ArgumentNullException(nameof(channels));
            FrameCount = frameCount;
            SampleRate = sampleRate;
        }

        public IReadOnlyList<AmigaChannelWaveformChannel> Channels { get; }

        public int FrameCount { get; }

        public int SampleRate { get; }
    }

    internal sealed class AmigaChannelWaveformChannel
    {
        public AmigaChannelWaveformChannel(int index, float[] samples, bool isActive)
        {
            Index = index;
            Samples = samples ?? throw new ArgumentNullException(nameof(samples));
            IsActive = isActive;
        }

        public int Index { get; }

        public float[] Samples { get; }

        public bool IsActive { get; }
    }
}
