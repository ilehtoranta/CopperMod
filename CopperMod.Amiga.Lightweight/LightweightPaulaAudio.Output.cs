namespace CopperMod.Amiga.Lightweight;

internal sealed partial class LightweightPaulaAudio
{
    internal const int OutputSampleRate = 48_000;
    internal const int PalCpuFrequency = 7_093_790;
    // The longest supported field contains fewer than 1,024 stereo samples.
    private short[] _renderingAudio = new short[2048];
    private short[] _completedAudio = new short[2048];
    private int _audioWriteIndex;
    private int _completedAudioCount;
    private long _outputCycle;
    private long _sampleTicksRemaining = PalCpuFrequency;
    private long _leftArea;
    private long _rightArea;
    private int _volumeAttachMask;
    private int _periodAttachMask;

    internal ReadOnlyMemory<short> Audio => _completedAudio.AsMemory(0, _completedAudioCount);

    private void ResetOutput()
    {
        Array.Clear(_renderingAudio);
        Array.Clear(_completedAudio);
        _audioWriteIndex = _completedAudioCount = 0;
        _outputCycle = 0;
        _sampleTicksRemaining = PalCpuFrequency;
        _leftArea = _rightArea = 0;
    }

    internal void CompleteFrame(long cycle)
    {
        EmitOutputTo(cycle);
        (_completedAudio, _renderingAudio) = (_renderingAudio, _completedAudio);
        _completedAudioCount = _audioWriteIndex;
        _audioWriteIndex = 0;
        // Fractional sample area and phase span fields without rounding or
        // feedback into emulated hardware. Every published sample is written.
    }

    private void EmitOutputTo(long cycle)
    {
        var ticks = (cycle - _outputCycle) * OutputSampleRate;
        System.Diagnostics.Debug.Assert(ticks >= 0);
        if (ticks == 0) return;
        _outputCycle = cycle;
        var muted = _volumeAttachMask | _periodAttachMask;
        var left = ((muted & 1) == 0 ? _channels[0].CurrentSample * _channels[0].Volume : 0) +
            ((muted & 8) == 0 ? _channels[3].CurrentSample * _channels[3].Volume : 0);
        var right = ((muted & 2) == 0 ? _channels[1].CurrentSample * _channels[1].Volume : 0) +
            ((muted & 4) == 0 ? _channels[2].CurrentSample * _channels[2].Volume : 0);
        if (ticks < _sampleTicksRemaining)
        {
            _leftArea += left * ticks;
            _rightArea += right * ticks;
            _sampleTicksRemaining -= ticks;
            return;
        }

        // Complete the sample that may contain earlier DAC levels.
        _leftArea += left * _sampleTicksRemaining;
        _rightArea += right * _sampleTicksRemaining;
        _renderingAudio[_audioWriteIndex++] = (short)(_leftArea * 2 / PalCpuFrequency);
        _renderingAudio[_audioWriteIndex++] = (short)(_rightArea * 2 / PalCpuFrequency);
        ticks -= _sampleTicksRemaining;

        // Whole samples at this level need neither integration nor division.
        // Two full-volume signed 8-bit channels fit exactly in int16.
        while (ticks >= PalCpuFrequency)
        {
            _renderingAudio[_audioWriteIndex++] = (short)(left * 2);
            _renderingAudio[_audioWriteIndex++] = (short)(right * 2);
            ticks -= PalCpuFrequency;
        }
        _sampleTicksRemaining = PalCpuFrequency - ticks;
        _leftArea = left * ticks;
        _rightArea = right * ticks;
    }

    private bool IsPeriodAttached(int channel) => (_periodAttachMask & (1 << channel)) != 0;
    private bool UsesHighByteRequest(int channel)
        => !IsPeriodAttached(channel) || (_volumeAttachMask & (1 << channel)) != 0;

    private void ApplyVolume(int source, ushort word)
    {
        if (source < 3 && (_volumeAttachMask & (1 << source)) != 0)
            _channels[source + 1].Volume = Math.Min(64, word & 0x7F);
    }

    private void ApplyPeriod(int source, ushort word)
    {
        if (source < 3 && IsPeriodAttached(source))
            _channels[source + 1].Period = word;
    }
}
