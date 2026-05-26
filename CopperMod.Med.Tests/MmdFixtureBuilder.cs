using System;
using System.Buffers.Binary;
using CopperMod.Med;

namespace CopperMod.Med.Tests;

internal static class MmdFixtureBuilder
{
    public static byte[] CreateMmd0Sample(
        byte command = 0,
        byte commandData = 0,
        byte tempo2 = 3,
        byte note = 25,
        byte instrument = 1,
        int lineCount = 1,
        byte songFlags = 0,
        sbyte playTranspose = 0,
        sbyte instrumentTranspose = 0,
        short sampleType = 0,
        byte[]? sampleData = null,
        int repeatOffset = 0,
        int repeatLength = 0)
    {
        var sampleCount = Math.Max(1, (int)instrument);
        var data = CreateBase(MmdConstants.Mmd0, 1, sampleCount);
        const int songOffset = 52;
        const int blockArrayOffset = songOffset + MmdConstants.SongLength;
        const int sampleArrayOffset = blockArrayOffset + 4;
        var blockOffset = sampleArrayOffset + (sampleCount * 4);
        var blockLength = 2 + (4 * lineCount * 3);
        var instrumentOffset = Align(blockOffset + blockLength);
        var sampleLength = sampleData?.Length ?? 64;
        var end = instrumentOffset + 6 + sampleLength;

        WriteCommonHeader(data, MmdConstants.Mmd0, end, songOffset, blockArrayOffset, sampleArrayOffset);
        WriteLegacySong(data, songOffset, 1, sampleCount, tempo2, songFlags);
        data[songOffset + 766] = unchecked((byte)playTranspose);
        var sampleInfoOffset = songOffset + ((instrument - 1) * 8);
        WriteU16(data, sampleInfoOffset, (ushort)(repeatOffset / 2));
        WriteU16(data, sampleInfoOffset + 2, (ushort)(repeatLength / 2));
        data[sampleInfoOffset + 7] = unchecked((byte)instrumentTranspose);
        WriteU32(data, blockArrayOffset, (uint)blockOffset);
        WriteU32(data, sampleArrayOffset + ((instrument - 1) * 4), (uint)instrumentOffset);
        WriteMmd0Block(data, blockOffset, lineCount, 4, note, instrument, command, commandData);
        WriteSampleInstrument(data, instrumentOffset, sampleLength, sampleType, sampleData);
        return Trim(data, end);
    }

    public static byte[] CreateMmd0FourVoiceSampleChord()
    {
        const int sampleCount = 4;
        var data = CreateBase(MmdConstants.Mmd0, 1, sampleCount);
        const int songOffset = 52;
        const int blockArrayOffset = songOffset + MmdConstants.SongLength;
        const int sampleArrayOffset = blockArrayOffset + 4;
        const int blockOffset = sampleArrayOffset + (sampleCount * 4);
        const int blockLength = 2 + (4 * 1 * 3);
        var instrumentOffset = Align(blockOffset + blockLength);
        const int sampleLength = 64;

        WriteCommonHeader(data, MmdConstants.Mmd0, instrumentOffset + sampleCount * (6 + sampleLength), songOffset, blockArrayOffset, sampleArrayOffset);
        WriteLegacySong(data, songOffset, 1, sampleCount, 3);
        for (var i = 0; i < sampleCount; i++)
        {
            var sampleInfoOffset = songOffset + (i * 8);
            WriteU16(data, sampleInfoOffset, 0);
            WriteU16(data, sampleInfoOffset + 2, sampleLength / 2);
            data[sampleInfoOffset + 6] = 64;
        }

        WriteU32(data, blockArrayOffset, (uint)blockOffset);
        data[blockOffset] = 4;
        data[blockOffset + 1] = 0;
        for (var track = 0; track < 4; track++)
        {
            WriteMmd0Cell(data, blockOffset + 2 + track * 3, (byte)(25 + track * 2), (byte)(track + 1), 0, 0);
        }

        var cursor = instrumentOffset;
        for (var i = 0; i < sampleCount; i++)
        {
            WriteU32(data, sampleArrayOffset + i * 4, (uint)cursor);
            WriteSampleInstrument(data, cursor, sampleLength);
            cursor += 6 + sampleLength;
        }

        return Trim(data, cursor);
    }

    public static byte[] CreateMmd0TwoBlockJumpLoop()
    {
        var data = CreateBase(MmdConstants.Mmd0, 2, 1);
        const int songOffset = 52;
        const int blockArrayOffset = songOffset + MmdConstants.SongLength;
        const int sampleArrayOffset = blockArrayOffset + 8;
        const int block0Offset = sampleArrayOffset + 4;
        const int block1Offset = block0Offset + 2 + (4 * 1 * 3);
        var instrumentOffset = Align(block1Offset + 2 + (4 * 1 * 3));
        var end = instrumentOffset + 6 + 64;

        WriteCommonHeader(data, MmdConstants.Mmd0, end, songOffset, blockArrayOffset, sampleArrayOffset);
        WriteLegacySong(data, songOffset, 2, 1, 1);
        data[songOffset + 508] = 0;
        data[songOffset + 509] = 1;
        WriteU32(data, blockArrayOffset, (uint)block0Offset);
        WriteU32(data, blockArrayOffset + 4, (uint)block1Offset);
        WriteU32(data, sampleArrayOffset, (uint)instrumentOffset);
        WriteMmd0Block(data, block0Offset, 1, 4, 0, 0, 0x0B, 0);
        WriteMmd0Block(data, block1Offset, 1, 4, 25, 1, 0, 0);
        WriteSampleInstrument(data, instrumentOffset, 64);
        return Trim(data, end);
    }

    public static byte[] CreateMmd0Portamento()
    {
        var data = CreateMmd0Sample(tempo2: 1, lineCount: 2);
        const int blockOffset = 52 + MmdConstants.SongLength + 4 + 4;
        WriteMmd0Cell(data, blockOffset + 2 + (4 * 3), 37, 1, 0x03, 4);
        return data;
    }

    public static byte[] CreateMmd0Synth()
    {
        var data = CreateBase(MmdConstants.Mmd0, 1, 1);
        const int songOffset = 52;
        const int blockArrayOffset = songOffset + MmdConstants.SongLength;
        const int sampleArrayOffset = blockArrayOffset + 4;
        const int blockOffset = sampleArrayOffset + 4;
        var instrumentOffset = Align(blockOffset + 2 + (4 * 1 * 3));
        var waveformOffset = instrumentOffset + 278 + 4;
        var waveformLength = 32;
        var end = waveformOffset + 2 + waveformLength;

        WriteCommonHeader(data, MmdConstants.Mmd0, end, songOffset, blockArrayOffset, sampleArrayOffset);
        WriteLegacySong(data, songOffset, 1, 1, 3);
        WriteU32(data, blockArrayOffset, (uint)blockOffset);
        WriteU32(data, sampleArrayOffset, (uint)instrumentOffset);
        WriteMmd0Block(data, blockOffset, 1, 4, 25, 1, 0, 0);

        WriteU32(data, instrumentOffset, 0);
        WriteI16(data, instrumentOffset + 4, -1);
        WriteU16(data, instrumentOffset + 14, 2);
        WriteU16(data, instrumentOffset + 16, 2);
        data[instrumentOffset + 18] = 1;
        data[instrumentOffset + 19] = 1;
        WriteU16(data, instrumentOffset + 20, 1);
        data[instrumentOffset + 22] = 64;
        data[instrumentOffset + 23] = 0xFF;
        data[instrumentOffset + 150] = 0;
        data[instrumentOffset + 151] = 0xFF;
        WriteU32(data, instrumentOffset + 278, (uint)(waveformOffset - instrumentOffset));
        WriteU16(data, waveformOffset, (ushort)(waveformLength / 2));
        WriteAlternatingSample(data, waveformOffset + 2, waveformLength);

        return Trim(data, end);
    }

    public static byte[] CreateMmd0SynthWaveformSwitch()
    {
        var data = CreateBase(MmdConstants.Mmd0, 1, 1);
        const int songOffset = 52;
        const int blockArrayOffset = songOffset + MmdConstants.SongLength;
        const int sampleArrayOffset = blockArrayOffset + 4;
        const int blockOffset = sampleArrayOffset + 4;
        var instrumentOffset = Align(blockOffset + 2 + (4 * 1 * 3));
        var waveformLength = 2048;
        var waveform0Offset = instrumentOffset + 278 + 8;
        var waveform1Offset = waveform0Offset + 2 + waveformLength;
        var end = waveform1Offset + 2 + waveformLength;

        WriteCommonHeader(data, MmdConstants.Mmd0, end, songOffset, blockArrayOffset, sampleArrayOffset);
        WriteLegacySong(data, songOffset, 1, 1, 3);
        WriteU32(data, blockArrayOffset, (uint)blockOffset);
        WriteU32(data, sampleArrayOffset, (uint)instrumentOffset);
        WriteMmd0Block(data, blockOffset, 1, 4, 25, 1, 0, 0);

        WriteU32(data, instrumentOffset, 0);
        WriteI16(data, instrumentOffset + 4, -1);
        WriteU16(data, instrumentOffset + 14, 2);
        WriteU16(data, instrumentOffset + 16, 3);
        data[instrumentOffset + 18] = 1;
        data[instrumentOffset + 19] = 1;
        WriteU16(data, instrumentOffset + 20, 2);
        data[instrumentOffset + 22] = 64;
        data[instrumentOffset + 23] = 0xFF;
        data[instrumentOffset + 150] = 0;
        data[instrumentOffset + 151] = 1;
        data[instrumentOffset + 152] = 0xFF;
        WriteU32(data, instrumentOffset + 278, (uint)(waveform0Offset - instrumentOffset));
        WriteU32(data, instrumentOffset + 282, (uint)(waveform1Offset - instrumentOffset));

        WriteU16(data, waveform0Offset, (ushort)(waveformLength / 2));
        WriteAlternatingSample(data, waveform0Offset + 2, waveformLength);
        WriteU16(data, waveform1Offset, (ushort)(waveformLength / 2));
        WriteAlternatingSample(data, waveform1Offset + 2, waveformLength);

        return Trim(data, end);
    }

    public static byte[] CreateMmd0SynthPeriodSlide()
    {
        var data = CreateBase(MmdConstants.Mmd0, 1, 1);
        const int songOffset = 52;
        const int blockArrayOffset = songOffset + MmdConstants.SongLength;
        const int sampleArrayOffset = blockArrayOffset + 4;
        const int blockOffset = sampleArrayOffset + 4;
        var instrumentOffset = Align(blockOffset + 2 + (4 * 1 * 3));
        var waveformOffset = instrumentOffset + 278 + 4;
        var waveformLength = 32;
        var end = waveformOffset + 2 + waveformLength;

        WriteCommonHeader(data, MmdConstants.Mmd0, end, songOffset, blockArrayOffset, sampleArrayOffset);
        WriteLegacySong(data, songOffset, 1, 1, 6);
        WriteU32(data, blockArrayOffset, (uint)blockOffset);
        WriteU32(data, sampleArrayOffset, (uint)instrumentOffset);
        WriteMmd0Block(data, blockOffset, 1, 4, 25, 1, 0, 0);

        WriteU32(data, instrumentOffset, 0);
        WriteI16(data, instrumentOffset + 4, -1);
        WriteU16(data, instrumentOffset + 14, 2);
        WriteU16(data, instrumentOffset + 16, 4);
        data[instrumentOffset + 18] = 1;
        data[instrumentOffset + 19] = 1;
        WriteU16(data, instrumentOffset + 20, 1);
        data[instrumentOffset + 22] = 64;
        data[instrumentOffset + 23] = 0xFF;
        data[instrumentOffset + 150] = 0;
        data[instrumentOffset + 151] = 0xF2;
        data[instrumentOffset + 152] = 4;
        data[instrumentOffset + 153] = 0xFF;
        WriteU32(data, instrumentOffset + 278, (uint)(waveformOffset - instrumentOffset));
        WriteU16(data, waveformOffset, (ushort)(waveformLength / 2));
        WriteAlternatingSample(data, waveformOffset + 2, waveformLength);

        return Trim(data, end);
    }

    public static byte[] CreateMmd0SynthVibrato()
    {
        var data = CreateBase(MmdConstants.Mmd0, 1, 1);
        const int songOffset = 52;
        const int blockArrayOffset = songOffset + MmdConstants.SongLength;
        const int sampleArrayOffset = blockArrayOffset + 4;
        const int blockOffset = sampleArrayOffset + 4;
        var instrumentOffset = Align(blockOffset + 2 + (4 * 1 * 3));
        var waveformOffset = instrumentOffset + 278 + 4;
        var waveformLength = 32;
        var end = waveformOffset + 2 + waveformLength;

        WriteCommonHeader(data, MmdConstants.Mmd0, end, songOffset, blockArrayOffset, sampleArrayOffset);
        WriteLegacySong(data, songOffset, 1, 1, 6);
        WriteU32(data, blockArrayOffset, (uint)blockOffset);
        WriteU32(data, sampleArrayOffset, (uint)instrumentOffset);
        WriteMmd0Block(data, blockOffset, 1, 4, 25, 1, 0, 0);

        WriteU32(data, instrumentOffset, 0);
        WriteI16(data, instrumentOffset + 4, -1);
        WriteU16(data, instrumentOffset + 14, 2);
        WriteU16(data, instrumentOffset + 16, 6);
        data[instrumentOffset + 18] = 1;
        data[instrumentOffset + 19] = 1;
        WriteU16(data, instrumentOffset + 20, 1);
        data[instrumentOffset + 22] = 64;
        data[instrumentOffset + 23] = 0xFF;
        data[instrumentOffset + 150] = 0xF4;
        data[instrumentOffset + 151] = 64;
        data[instrumentOffset + 152] = 0xF5;
        data[instrumentOffset + 153] = 15;
        data[instrumentOffset + 154] = 0xFF;
        data[instrumentOffset + 155] = 0;
        WriteU32(data, instrumentOffset + 278, (uint)(waveformOffset - instrumentOffset));
        WriteU16(data, waveformOffset, (ushort)(waveformLength / 2));
        WriteAlternatingSample(data, waveformOffset + 2, waveformLength);

        return Trim(data, end);
    }

    public static byte[] CreateMmd0SynthEnvelopeOneShot()
    {
        var waveform = CreateEnvelopeWaveform();
        return CreateMmd0SynthCustom(
            new byte[] { 0xF4, 0, 0xFF },
            new byte[] { 0, 0xFF },
            new[] { waveform },
            tempo2: 200);
    }

    public static byte[] CreateMmd0SynthEnvelopeLoop()
    {
        var waveform = CreateEnvelopeWaveform();
        return CreateMmd0SynthCustom(
            new byte[] { 0xF5, 0, 0xFF },
            new byte[] { 0, 0xFF },
            new[] { waveform },
            tempo2: 200);
    }

    public static byte[] CreateMmd0SynthEnvelopeStop()
    {
        var waveform = CreateEnvelopeWaveform();
        return CreateMmd0SynthCustom(
            new byte[] { 0xF4, 0, 0xF6, 40, 0xFF },
            new byte[] { 0, 0xFF },
            new[] { waveform },
            tempo2: 200);
    }

    public static byte[] CreateMmd0SynthArpeggio()
    {
        return CreateMmd0SynthCustom(
            new byte[] { 64, 0xFF },
            new byte[] { 0xFC, 0, 4, 7, 0xFD, 0xFF },
            new[] { CreateEnvelopeWaveform() },
            tempo2: 200);
    }

    public static byte[] CreateMmd0SynthWaveformSpeedDelay()
    {
        return CreateMmd0SynthCustom(
            new byte[] { 64, 0xFF },
            new byte[] { 1, 0xFF },
            new[] { CreateEnvelopeWaveform(), CreateEnvelopeWaveform() },
            tempo2: 200,
            waveformSpeed: 3);
    }

    public static byte[] CreateMmd0HybridToDelayedSynth()
    {
        const int sampleCount = 2;
        const int lineCount = 2;
        const int hybridSampleLength = 96;
        const int synthWaveformLength = 128;
        var data = CreateBase(MmdConstants.Mmd0, 1, sampleCount);
        const int songOffset = 52;
        const int blockArrayOffset = songOffset + MmdConstants.SongLength;
        const int sampleArrayOffset = blockArrayOffset + 4;
        const int blockOffset = sampleArrayOffset + sampleCount * 4;
        const int blockLength = 2 + (4 * lineCount * 3);
        var hybridOffset = Align(blockOffset + blockLength);
        var hybridWaveformOffset = hybridOffset + 278 + 4;
        var synthOffset = Align(hybridWaveformOffset + 6 + hybridSampleLength);
        var synthWaveformTableOffset = synthOffset + 278;
        var synthWaveform0Offset = synthWaveformTableOffset + 8;
        var synthWaveform1Offset = synthWaveform0Offset + 2 + synthWaveformLength;
        var end = synthWaveform1Offset + 2 + synthWaveformLength;

        WriteCommonHeader(data, MmdConstants.Mmd0, end, songOffset, blockArrayOffset, sampleArrayOffset);
        WriteLegacySong(data, songOffset, 1, sampleCount, 1);
        data[songOffset + 6] = 64;
        data[songOffset + 14] = 64;
        WriteU16(data, songOffset, 0);
        WriteU16(data, songOffset + 2, hybridSampleLength / 2);

        WriteU32(data, blockArrayOffset, (uint)blockOffset);
        WriteU32(data, sampleArrayOffset, (uint)hybridOffset);
        WriteU32(data, sampleArrayOffset + 4, (uint)synthOffset);
        data[blockOffset] = 4;
        data[blockOffset + 1] = lineCount - 1;
        WriteMmd0Cell(data, blockOffset + 2, 25, 1, 0, 0);
        WriteMmd0Cell(data, blockOffset + 2 + (4 * 3), 25, 2, 0, 0);

        WriteU32(data, hybridOffset, 0);
        WriteI16(data, hybridOffset + 4, -2);
        WriteU16(data, hybridOffset + 10, 0);
        WriteU16(data, hybridOffset + 12, hybridSampleLength / 2);
        WriteU16(data, hybridOffset + 14, 2);
        WriteU16(data, hybridOffset + 16, 1);
        data[hybridOffset + 18] = 1;
        data[hybridOffset + 19] = 1;
        WriteU16(data, hybridOffset + 20, 1);
        data[hybridOffset + 22] = 64;
        data[hybridOffset + 23] = 0xFF;
        data[hybridOffset + 150] = 0xFF;
        WriteU32(data, hybridOffset + 278, (uint)(hybridWaveformOffset - hybridOffset));
        WriteSampleInstrument(data, hybridWaveformOffset, hybridSampleLength, sampleData: Enumerable.Repeat((byte)0x40, hybridSampleLength).ToArray());

        WriteU32(data, synthOffset, 0);
        WriteI16(data, synthOffset + 4, -1);
        WriteU16(data, synthOffset + 14, 2);
        WriteU16(data, synthOffset + 16, 2);
        data[synthOffset + 18] = 1;
        data[synthOffset + 19] = 3;
        WriteU16(data, synthOffset + 20, 2);
        data[synthOffset + 22] = 64;
        data[synthOffset + 23] = 0xFF;
        data[synthOffset + 150] = 1;
        data[synthOffset + 151] = 0xFF;
        WriteU32(data, synthWaveformTableOffset, (uint)(synthWaveform0Offset - synthOffset));
        WriteU32(data, synthWaveformTableOffset + 4, (uint)(synthWaveform1Offset - synthOffset));
        WriteU16(data, synthWaveform0Offset, synthWaveformLength / 2);
        WriteAlternatingSample(data, synthWaveform0Offset + 2, synthWaveformLength);
        WriteU16(data, synthWaveform1Offset, synthWaveformLength / 2);
        WriteAlternatingSample(data, synthWaveform1Offset + 2, synthWaveformLength);

        return Trim(data, end);
    }

    public static byte[] CreateMmd0SynthRetrigger()
    {
        return CreateMmd0SynthCustom(
            new byte[] { 64, 0xFF },
            new byte[] { 0, 0xFF },
            new[] { CreateEnvelopeWaveform() },
            tempo2: 1,
            secondLineNote: 25);
    }

    public static byte[] CreateMmd1WithCommandPage()
    {
        return CreateMmd1OrLater(MmdConstants.Mmd1, includeCommandPage: true, useMmd2Song: false);
    }

    public static byte[] CreateMmd1SampleCommand(byte command, byte commandData, byte[]? sampleData = null)
    {
        return CreateMmd1OrLater(
            MmdConstants.Mmd1,
            includeCommandPage: false,
            useMmd2Song: false,
            command: command,
            commandData: commandData,
            sampleData: sampleData);
    }

    public static byte[] CreateMmd1SampleOffsetCarryFixture()
    {
        const int sampleCount = 2;
        const int lineCount = 4;
        const int sampleLength = 512;
        var data = CreateBase(MmdConstants.Mmd1, 1, sampleCount);
        const int songOffset = 52;
        const int blockArrayOffset = songOffset + MmdConstants.SongLength;
        const int sampleArrayOffset = blockArrayOffset + 4;
        const int blockOffset = sampleArrayOffset + sampleCount * 4;
        const int blockDataLength = 8 + (4 * lineCount * 4);
        var instrument1Offset = Align(blockOffset + blockDataLength);
        var instrument2Offset = instrument1Offset + 6 + sampleLength;
        var end = instrument2Offset + 6 + sampleLength;

        WriteCommonHeader(data, MmdConstants.Mmd1, end, songOffset, blockArrayOffset, sampleArrayOffset);
        WriteLegacySong(data, songOffset, 1, sampleCount, 1);
        data[songOffset + 8 + 6] = 64;
        WriteU32(data, blockArrayOffset, (uint)blockOffset);
        WriteU32(data, sampleArrayOffset, (uint)instrument1Offset);
        WriteU32(data, sampleArrayOffset + 4, (uint)instrument2Offset);

        WriteU16(data, blockOffset, 4);
        WriteU16(data, blockOffset + 2, lineCount - 1);
        WriteU32(data, blockOffset + 4, 0);
        WriteMmd1Cell(data, blockOffset + 8, 25, 1, 0x19, 0x01);
        WriteMmd1Cell(data, blockOffset + 8 + (4 * 4), 25, 0, 0, 0);
        WriteMmd1Cell(data, blockOffset + 8 + (4 * 4 * 2), 0, 2, 0, 0);
        WriteMmd1Cell(data, blockOffset + 8 + (4 * 4 * 3), 25, 0, 0, 0);

        WriteSampleInstrument(data, instrument1Offset, sampleLength, sampleData: Enumerable.Repeat((byte)0x40, sampleLength).ToArray());
        WriteSampleInstrument(data, instrument2Offset, sampleLength, sampleData: Enumerable.Repeat((byte)0x20, sampleLength).ToArray());
        return Trim(data, end);
    }

    public static byte[] CreateMmd2OrMmd3(uint id, short sampleType = 0, byte[]? sampleData = null)
    {
        return CreateMmd1OrLater(id, includeCommandPage: true, useMmd2Song: true, sampleType, sampleData);
    }

    private static byte[] CreateMmd1OrLater(
        uint id,
        bool includeCommandPage,
        bool useMmd2Song,
        short sampleType = 0,
        byte[]? sampleData = null,
        byte command = 0,
        byte commandData = 0)
    {
        var data = CreateBase(id, 1, 1);
        const int songOffset = 52;
        const int blockArrayOffset = songOffset + MmdConstants.SongLength;
        const int sampleArrayOffset = blockArrayOffset + 4;
        var cursor = sampleArrayOffset + 4;

        var trackVolumeOffset = cursor;
        cursor += useMmd2Song ? 4 : 0;
        var trackPanOffset = cursor;
        cursor += useMmd2Song ? 4 : 0;
        var sectionTableOffset = cursor;
        cursor += useMmd2Song ? 2 : 0;
        var playSequenceTableOffset = cursor;
        cursor += useMmd2Song ? 4 : 0;
        var playSequenceOffset = cursor;
        cursor += useMmd2Song ? 44 : 0;

        var blockOffset = Align(cursor);
        var blockDataLength = 8 + (4 * 1 * 4);
        var blockInfoOffset = includeCommandPage ? Align(blockOffset + blockDataLength) : 0;
        var pageTableOffset = includeCommandPage ? blockInfoOffset + 36 : 0;
        var commandPageOffset = includeCommandPage ? pageTableOffset + 8 : 0;
        var instrumentOffset = Align(includeCommandPage ? commandPageOffset + 8 : blockOffset + blockDataLength);
        var sampleLength = sampleData?.Length ?? 64;
        var end = instrumentOffset + 6 + sampleLength;

        WriteCommonHeader(data, id, end, songOffset, blockArrayOffset, sampleArrayOffset);
        if (useMmd2Song)
        {
            WriteMmd2Song(data, songOffset, trackVolumeOffset, trackPanOffset, sectionTableOffset, playSequenceTableOffset, playSequenceOffset);
        }
        else
        {
            WriteLegacySong(data, songOffset, 1, 1, 3);
        }

        WriteU32(data, blockArrayOffset, (uint)blockOffset);
        WriteU32(data, sampleArrayOffset, (uint)instrumentOffset);
        WriteMmd1Block(data, blockOffset, blockInfoOffset, 25, 1, command, commandData);
        if (includeCommandPage)
        {
            WriteBlockInfoWithCommandPage(data, blockInfoOffset, pageTableOffset, commandPageOffset);
        }

        WriteSampleInstrument(data, instrumentOffset, sampleLength, sampleType, sampleData);
        return Trim(data, end);
    }

    private static byte[] CreateMmd0SynthCustom(
        byte[] volumeSequence,
        byte[] waveformSequence,
        byte[][] waveforms,
        byte tempo2,
        byte volumeSpeed = 1,
        byte waveformSpeed = 1,
        byte? secondLineNote = null)
    {
        var data = CreateBase(MmdConstants.Mmd0, 1, 1);
        const int songOffset = 52;
        const int blockArrayOffset = songOffset + MmdConstants.SongLength;
        const int sampleArrayOffset = blockArrayOffset + 4;
        const int blockOffset = sampleArrayOffset + 4;
        var lineCount = secondLineNote.HasValue ? 2 : 1;
        var instrumentOffset = Align(blockOffset + 2 + (4 * lineCount * 3));
        var waveformPointerTableOffset = instrumentOffset + 278;
        var waveformOffset = waveformPointerTableOffset + (waveforms.Length * 4);
        var cursor = waveformOffset;
        for (var i = 0; i < waveforms.Length; i++)
        {
            cursor += 2 + waveforms[i].Length;
        }

        WriteCommonHeader(data, MmdConstants.Mmd0, cursor, songOffset, blockArrayOffset, sampleArrayOffset);
        WriteLegacySong(data, songOffset, 1, 1, tempo2);
        WriteU32(data, blockArrayOffset, (uint)blockOffset);
        WriteU32(data, sampleArrayOffset, (uint)instrumentOffset);
        WriteMmd0Block(data, blockOffset, lineCount, 4, 25, 1, 0, 0);
        if (secondLineNote.HasValue)
        {
            WriteMmd0Cell(data, blockOffset + 2 + (4 * 3), secondLineNote.Value, 1, 0, 0);
        }

        WriteU32(data, instrumentOffset, 0);
        WriteI16(data, instrumentOffset + 4, -1);
        WriteU16(data, instrumentOffset + 14, (ushort)volumeSequence.Length);
        WriteU16(data, instrumentOffset + 16, (ushort)waveformSequence.Length);
        data[instrumentOffset + 18] = volumeSpeed;
        data[instrumentOffset + 19] = waveformSpeed;
        WriteU16(data, instrumentOffset + 20, (ushort)waveforms.Length);
        Array.Copy(volumeSequence, 0, data, instrumentOffset + 22, volumeSequence.Length);
        Array.Copy(waveformSequence, 0, data, instrumentOffset + 150, waveformSequence.Length);

        cursor = waveformOffset;
        for (var i = 0; i < waveforms.Length; i++)
        {
            WriteU32(data, waveformPointerTableOffset + i * 4, (uint)(cursor - instrumentOffset));
            WriteU16(data, cursor, (ushort)(waveforms[i].Length / 2));
            Array.Copy(waveforms[i], 0, data, cursor + 2, waveforms[i].Length);
            cursor += 2 + waveforms[i].Length;
        }

        return Trim(data, cursor);
    }

    private static byte[] CreateEnvelopeWaveform()
    {
        var waveform = new byte[128];
        waveform[0] = 0x80;
        waveform[1] = 0;
        waveform[2] = 0x7F;
        waveform[127] = 0x7F;
        return waveform;
    }

    private static byte[] CreateBase(uint id, int numBlocks, int numSamples)
    {
        _ = id;
        _ = numBlocks;
        _ = numSamples;
        return new byte[8192];
    }

    private static void WriteCommonHeader(byte[] data, uint id, int length, int songOffset, int blockArrayOffset, int sampleArrayOffset)
    {
        WriteU32(data, 0, id);
        WriteU32(data, 4, (uint)length);
        WriteU32(data, 8, (uint)songOffset);
        WriteU32(data, 16, (uint)blockArrayOffset);
        WriteU32(data, 24, (uint)sampleArrayOffset);
    }

    private static void WriteLegacySong(byte[] data, int songOffset, int blockCount, int sampleCount, byte tempo2, byte flags = 0)
    {
        data[songOffset + 6] = 64;
        WriteU16(data, songOffset + 504, (ushort)blockCount);
        WriteU16(data, songOffset + 506, (ushort)blockCount);
        for (var i = 0; i < blockCount; i++)
        {
            data[songOffset + 508 + i] = (byte)i;
        }

        WriteU16(data, songOffset + 764, 33);
        data[songOffset + 767] = flags;
        data[songOffset + 769] = tempo2;
        for (var i = 0; i < 16; i++)
        {
            data[songOffset + 770 + i] = 64;
        }

        data[songOffset + 786] = 64;
        data[songOffset + 787] = (byte)sampleCount;
    }

    private static void WriteMmd2Song(
        byte[] data,
        int songOffset,
        int trackVolumeOffset,
        int trackPanOffset,
        int sectionTableOffset,
        int playSequenceTableOffset,
        int playSequenceOffset)
    {
        data[songOffset + 6] = 64;
        WriteU16(data, songOffset + 504, 1);
        WriteU16(data, songOffset + 506, 1);
        WriteU32(data, songOffset + 508, (uint)playSequenceTableOffset);
        WriteU32(data, songOffset + 512, (uint)sectionTableOffset);
        WriteU32(data, songOffset + 516, (uint)trackVolumeOffset);
        WriteU16(data, songOffset + 520, 4);
        WriteU16(data, songOffset + 522, 1);
        WriteU32(data, songOffset + 524, (uint)trackPanOffset);
        WriteU32(data, songOffset + 528, MmdConstants.Flag3Stereo | MmdConstants.Flag3FreePan);
        WriteU16(data, songOffset + 532, 100);
        WriteU16(data, songOffset + 534, 4);
        WriteU16(data, songOffset + 764, 33);
        data[songOffset + 768] = MmdConstants.Flag2Mix;
        data[songOffset + 769] = 3;
        data[songOffset + 786] = 64;
        data[songOffset + 787] = 1;

        data[trackVolumeOffset] = 32;
        data[trackVolumeOffset + 1] = 48;
        data[trackVolumeOffset + 2] = 64;
        data[trackVolumeOffset + 3] = 64;
        data[trackPanOffset] = unchecked((byte)-16);
        data[trackPanOffset + 1] = 16;
        data[trackPanOffset + 2] = unchecked((byte)-16);
        data[trackPanOffset + 3] = 16;
        WriteU16(data, sectionTableOffset, 0);
        WriteU32(data, playSequenceTableOffset, (uint)playSequenceOffset);
        WriteU16(data, playSequenceOffset + 40, 1);
        WriteU16(data, playSequenceOffset + 42, 0);
    }

    private static void WriteMmd0Block(byte[] data, int offset, int lines, int tracks, byte note, byte instrument, byte command, byte commandData)
    {
        data[offset] = (byte)tracks;
        data[offset + 1] = (byte)(lines - 1);
        for (var line = 0; line < lines; line++)
        {
            for (var track = 0; track < tracks; track++)
            {
                var cellOffset = offset + 2 + ((line * tracks) + track) * 3;
                if (line == 0 && track == 0)
                {
                    WriteMmd0Cell(data, cellOffset, note, instrument, command, commandData);
                }
            }
        }
    }

    private static void WriteMmd0Cell(byte[] data, int offset, byte note, byte instrument, byte command, byte commandData)
    {
        data[offset] = (byte)((note & 0x3F) |
            ((instrument & 0x10) != 0 ? 0x80 : 0x00) |
            ((instrument & 0x20) != 0 ? 0x40 : 0x00));
        data[offset + 1] = (byte)(((instrument & 0x0F) << 4) | (command & 0x0F));
        data[offset + 2] = commandData;
    }

    private static void WriteMmd1Block(byte[] data, int offset, int infoOffset, byte note, byte instrument, byte command, byte commandData)
    {
        WriteU16(data, offset, 4);
        WriteU16(data, offset + 2, 0);
        WriteU32(data, offset + 4, (uint)infoOffset);
        WriteMmd1Cell(data, offset + 8, note, instrument, command, commandData);
    }

    private static void WriteMmd1Cell(byte[] data, int offset, byte note, byte instrument, byte command, byte commandData)
    {
        data[offset] = note;
        data[offset + 1] = instrument;
        data[offset + 2] = command;
        data[offset + 3] = commandData;
    }

    private static void WriteBlockInfoWithCommandPage(byte[] data, int infoOffset, int pageTableOffset, int commandPageOffset)
    {
        WriteU32(data, infoOffset + 12, (uint)pageTableOffset);
        WriteU16(data, pageTableOffset, 1);
        WriteU32(data, pageTableOffset + 4, (uint)commandPageOffset);
        data[commandPageOffset] = 0x0C;
        data[commandPageOffset + 1] = 32;
    }

    private static void WriteSampleInstrument(byte[] data, int offset, int sampleLength, short sampleType = 0, byte[]? sampleData = null)
    {
        WriteU32(data, offset, (uint)sampleLength);
        WriteI16(data, offset + 4, sampleType);
        if (sampleData != null)
        {
            Array.Copy(sampleData, 0, data, offset + 6, sampleLength);
            return;
        }

        WriteAlternatingSample(data, offset + 6, sampleLength);
    }

    private static void WriteAlternatingSample(byte[] data, int offset, int length)
    {
        for (var i = 0; i < length; i++)
        {
            data[offset + i] = (byte)(i % 2 == 0 ? 96 : unchecked((byte)-96));
        }
    }

    private static int Align(int value)
    {
        return (value + 1) & ~1;
    }

    private static byte[] Trim(byte[] data, int length)
    {
        var copy = new byte[length];
        Array.Copy(data, copy, length);
        return copy;
    }

    private static void WriteU16(byte[] data, int offset, ushort value)
    {
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(offset, 2), value);
    }

    private static void WriteI16(byte[] data, int offset, short value)
    {
        BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(offset, 2), value);
    }

    private static void WriteU32(byte[] data, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset, 4), value);
    }
}
