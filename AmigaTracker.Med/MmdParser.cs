using System;
using System.Collections.Generic;
using System.Text;
using AmigaTracker.Abstractions;

namespace AmigaTracker.Med
{
    internal static class MmdParser
    {
        public static MmdModule Parse(byte[] data)
        {
            var span = (ReadOnlySpan<byte>)data;
            var id = BigEndian.ReadUInt32(span, 0, "MMD id");
            var version = GetVersion(id);
            var moduleLength = checked((int)BigEndian.ReadUInt32(span, 4, "module length"));
            if (moduleLength > data.Length)
            {
                throw new ModuleLoadException("The MMD header length is larger than the supplied data.");
            }

            var module = new MmdModule(data, version, moduleLength);
            ParseSong(module);
            ParseExpansion(module);
            ParseBlocks(module);
            ParseInstruments(module);
            EnsureTrackDefaults(module);
            return module;
        }

        private static MmdVersion GetVersion(uint id)
        {
            switch (id)
            {
                case MmdConstants.Mmd0:
                    return MmdVersion.Mmd0;
                case MmdConstants.Mmd1:
                    return MmdVersion.Mmd1;
                case MmdConstants.Mmd2:
                    return MmdVersion.Mmd2;
                case MmdConstants.Mmd3:
                    return MmdVersion.Mmd3;
                default:
                    throw new UnsupportedModuleFormatException("Unsupported MMD id.");
            }
        }

        private static void ParseSong(MmdModule module)
        {
            var data = (ReadOnlySpan<byte>)module.Data;
            var songOffset = BigEndian.ReadPointer(data, 8, "song pointer");
            if (songOffset == 0)
            {
                throw new ModuleLoadException("The required MMD song structure pointer is null.");
            }

            BigEndian.RequireRange(data, songOffset, MmdConstants.SongLength, "song structure");

            var song = new MmdSongData
            {
                HeaderOffset = 0,
                SongOffset = songOffset,
                BlockArrayOffset = BigEndian.ReadPointer(data, 16, "block array pointer"),
                SampleArrayOffset = BigEndian.ReadPointer(data, 24, "instrument array pointer"),
                ExpansionOffset = BigEndian.ReadPointer(data, 32, "expansion pointer"),
                NumBlocks = BigEndian.ReadUInt16(data, songOffset + 504, "numblocks"),
                SongLength = BigEndian.ReadUInt16(data, songOffset + 506, "songlen"),
                DefaultTempo = BigEndian.ReadUInt16(data, songOffset + 764, "deftempo"),
                PlayTranspose = BigEndian.ReadSByte(data, songOffset + 766, "play transpose"),
                Flags = BigEndian.ReadByte(data, songOffset + 767, "flags"),
                Flags2 = BigEndian.ReadByte(data, songOffset + 768, "flags2"),
                Tempo2 = BigEndian.ReadByte(data, songOffset + 769, "tempo2"),
                MasterVolume = BigEndian.ReadByte(data, songOffset + 786, "master volume"),
                NumSamples = BigEndian.ReadByte(data, songOffset + 787, "numsamples")
            };

            if (song.DefaultTempo <= 0)
            {
                song.DefaultTempo = 33;
                module.Diagnostics.Add(new ModuleDiagnostic(ModuleDiagnosticSeverity.Warning, "Default tempo was zero; using MED default tempo 33.", "MMD_TEMPO_ZERO"));
            }

            if (song.Tempo2 == 0)
            {
                song.Tempo2 = 1;
                module.Diagnostics.Add(new ModuleDiagnostic(ModuleDiagnosticSeverity.Warning, "Secondary tempo was zero; using one timing pulse per row.", "MMD_TEMPO2_ZERO"));
            }

            ParseSampleInfos(data, songOffset, song);

            if (module.Version >= MmdVersion.Mmd2)
            {
                ParseMmd2SongFields(module, song);
            }
            else
            {
                BigEndian.RequireRange(data, songOffset + 508, 256, "legacy play sequence");
                Array.Copy(module.Data, songOffset + 508, song.LegacyPlaySequence, 0, 256);

                for (var i = 0; i < 16; i++)
                {
                    song.TrackVolumes[i] = BigEndian.ReadByte(data, songOffset + 770 + i, "track volume");
                    song.TrackPans[i] = GetDefaultPan(i);
                }

                song.NumTracks = (song.Flags & MmdConstants.Flag8Channel) != 0 ? 8 : 4;
                song.MixingChannels = song.NumTracks;
            }

            module.Song = song;
        }

        private static void ParseSampleInfos(ReadOnlySpan<byte> data, int songOffset, MmdSongData song)
        {
            for (var i = 0; i < song.SampleInfos.Length; i++)
            {
                var offset = songOffset + i * 8;
                song.SampleInfos[i] = new MmdSampleInfo
                {
                    RepeatOffset = BigEndian.ReadUInt16(data, offset, "sample repeat") * 2,
                    RepeatLength = BigEndian.ReadUInt16(data, offset + 2, "sample repeat length") * 2,
                    MidiChannel = BigEndian.ReadByte(data, offset + 4, "sample midi channel"),
                    MidiPreset = BigEndian.ReadByte(data, offset + 5, "sample midi preset"),
                    DefaultVolume = BigEndian.ReadByte(data, offset + 6, "sample volume"),
                    Transpose = BigEndian.ReadSByte(data, offset + 7, "sample transpose")
                };
            }
        }

        private static void ParseMmd2SongFields(MmdModule module, MmdSongData song)
        {
            var data = (ReadOnlySpan<byte>)module.Data;
            var baseOffset = song.SongOffset;
            var playSeqTablePtr = BigEndian.ReadPointer(data, baseOffset + 508, "playseq table pointer");
            var sectionTablePtr = BigEndian.ReadPointer(data, baseOffset + 512, "section table pointer");
            var trackVolumePtr = BigEndian.ReadPointer(data, baseOffset + 516, "track volume pointer");
            song.NumTracks = BigEndian.ReadUInt16(data, baseOffset + 520, "numtracks");
            song.NumPlaySequences = BigEndian.ReadUInt16(data, baseOffset + 522, "numpseqs");
            var trackPanPtr = BigEndian.ReadPointer(data, baseOffset + 524, "track pan pointer");
            song.Flags3 = BigEndian.ReadUInt32(data, baseOffset + 528, "flags3");
            song.VolumeAdjust = BigEndian.ReadUInt16(data, baseOffset + 532, "voladj");
            song.MixingChannels = BigEndian.ReadUInt16(data, baseOffset + 534, "channels");
            song.MixEchoType = BigEndian.ReadByte(data, baseOffset + 536, "mix echo type");
            song.MixEchoDepth = BigEndian.ReadByte(data, baseOffset + 537, "mix echo depth");
            song.MixEchoLength = BigEndian.ReadUInt16(data, baseOffset + 538, "mix echo length");
            song.MixStereoSeparation = BigEndian.ReadSByte(data, baseOffset + 540, "mix stereo separation");

            if (song.NumTracks <= 0)
            {
                song.NumTracks = song.MixingChannels > 0 ? song.MixingChannels : 4;
            }

            if (song.MixingChannels == 0)
            {
                song.MixingChannels = 4;
            }

            song.TrackVolumes = new byte[Math.Max(song.NumTracks, 1)];
            for (var i = 0; i < song.TrackVolumes.Length; i++)
            {
                song.TrackVolumes[i] = 64;
            }

            if (trackVolumePtr != 0 && BigEndian.HasRange(data, trackVolumePtr, song.TrackVolumes.Length))
            {
                Array.Copy(module.Data, trackVolumePtr, song.TrackVolumes, 0, song.TrackVolumes.Length);
            }

            song.TrackPans = new sbyte[Math.Max(song.NumTracks, 1)];
            for (var i = 0; i < song.TrackPans.Length; i++)
            {
                song.TrackPans[i] = GetDefaultPan(i);
            }

            if (trackPanPtr != 0 && BigEndian.HasRange(data, trackPanPtr, song.TrackPans.Length))
            {
                for (var i = 0; i < song.TrackPans.Length; i++)
                {
                    song.TrackPans[i] = unchecked((sbyte)module.Data[trackPanPtr + i]);
                }
            }

            if (sectionTablePtr != 0 && song.SongLength > 0)
            {
                BigEndian.RequireRange(data, sectionTablePtr, song.SongLength * 2, "section table");
                for (var i = 0; i < song.SongLength; i++)
                {
                    song.SectionTable.Add(BigEndian.ReadUInt16(data, sectionTablePtr + i * 2, "section entry"));
                }
            }

            if (playSeqTablePtr != 0 && song.NumPlaySequences > 0)
            {
                BigEndian.RequireRange(data, playSeqTablePtr, song.NumPlaySequences * 4, "playseq table");
                for (var i = 0; i < song.NumPlaySequences; i++)
                {
                    var playSeqPtr = BigEndian.ReadPointer(data, playSeqTablePtr + i * 4, "playseq pointer");
                    if (playSeqPtr == 0)
                    {
                        song.PlaySequences.Add(new MmdPlaySequence());
                    }
                    else
                    {
                        song.PlaySequences.Add(ParsePlaySequence(data, playSeqPtr));
                    }
                }
            }
        }

        private static MmdPlaySequence ParsePlaySequence(ReadOnlySpan<byte> data, int offset)
        {
            BigEndian.RequireRange(data, offset, 42, "playseq");
            var sequence = new MmdPlaySequence
            {
                Name = ReadFixedString(data, offset, 32)
            };

            var length = BigEndian.ReadUInt16(data, offset + 40, "playseq length");
            BigEndian.RequireRange(data, offset + 42, length * 2, "playseq block list");
            for (var i = 0; i < length; i++)
            {
                var block = BigEndian.ReadUInt16(data, offset + 42 + i * 2, "playseq block");
                if (block <= 0x7FFF)
                {
                    sequence.Blocks.Add(block);
                }
            }

            return sequence;
        }

        private static void ParseExpansion(MmdModule module)
        {
            var data = (ReadOnlySpan<byte>)module.Data;
            var expOffset = module.Song.ExpansionOffset;
            if (expOffset == 0)
            {
                InferLegacyLoopFlags(module);
                return;
            }

            if (!BigEndian.HasRange(data, expOffset, 76))
            {
                module.Diagnostics.Add(new ModuleDiagnostic(ModuleDiagnosticSeverity.Warning, "Expansion structure points outside the module and was ignored.", "MMD_EXP_RANGE"));
                InferLegacyLoopFlags(module);
                return;
            }

            var expSamplePtr = BigEndian.ReadPointer(data, expOffset + 4, "InstrExt pointer");
            var expEntries = BigEndian.ReadUInt16(data, expOffset + 8, "InstrExt entries");
            var expEntrySize = BigEndian.ReadUInt16(data, expOffset + 10, "InstrExt entry size");
            if (expSamplePtr != 0 && expEntries > 0 && expEntrySize > 0)
            {
                ParseInstrumentExtensions(module, expSamplePtr, expEntries, expEntrySize);
            }
            else
            {
                InferLegacyLoopFlags(module);
            }

            var infoPtr = BigEndian.ReadPointer(data, expOffset + 20, "instrument info pointer");
            var infoEntries = BigEndian.ReadUInt16(data, expOffset + 24, "instrument info entries");
            var infoEntrySize = BigEndian.ReadUInt16(data, expOffset + 26, "instrument info entry size");
            if (infoPtr != 0 && infoEntries > 0 && infoEntrySize > 0)
            {
                ParseInstrumentNames(module, infoPtr, infoEntries, infoEntrySize);
            }

            for (var i = 0; i < 4; i++)
            {
                module.Song.ChannelSplit[i] = BigEndian.ReadByte(data, expOffset + 36 + i, "channel split");
            }

            var songNamePtr = BigEndian.ReadPointer(data, expOffset + 44, "song name pointer");
            var songNameLength = checked((int)BigEndian.ReadUInt32(data, expOffset + 48, "song name length"));
            if (songNamePtr != 0 && songNameLength > 0 && BigEndian.HasRange(data, songNamePtr, songNameLength))
            {
                module.Song.SongName = ReadNullTerminatedString(data, songNamePtr, songNameLength);
            }
        }

        private static void ParseInstrumentExtensions(MmdModule module, int offset, int entries, int entrySize)
        {
            var data = (ReadOnlySpan<byte>)module.Data;
            var count = Math.Min(entries, MmdConstants.MaxLegacyInstruments);
            for (var i = 0; i < count; i++)
            {
                var entryOffset = offset + i * entrySize;
                if (!BigEndian.HasRange(data, entryOffset, Math.Min(entrySize, 1)))
                {
                    module.Diagnostics.Add(new ModuleDiagnostic(ModuleDiagnosticSeverity.Warning, "InstrExt table ended before all advertised entries.", "MMD_INSTREXT_RANGE"));
                    return;
                }

                var extension = module.Song.SampleInfos[i].Extension;
                if (entrySize > 0)
                {
                    extension.Hold = BigEndian.ReadByte(data, entryOffset, "InstrExt hold");
                }

                if (entrySize > 1)
                {
                    extension.Decay = BigEndian.ReadByte(data, entryOffset + 1, "InstrExt decay");
                }

                if (entrySize > 3)
                {
                    extension.Finetune = BigEndian.ReadSByte(data, entryOffset + 3, "InstrExt finetune");
                }

                if (entrySize > 5)
                {
                    extension.Flags = BigEndian.ReadByte(data, entryOffset + 5, "InstrExt flags");
                }
                else if (module.Song.SampleInfos[i].RepeatLength > 2)
                {
                    extension.Flags |= MmdConstants.InstrFlagLoop;
                }

                if (entrySize > 8)
                {
                    extension.OutputDevice = BigEndian.ReadByte(data, entryOffset + 8, "InstrExt output device");
                }

                if (entrySize >= 18)
                {
                    extension.LongRepeatOffset = checked((int)BigEndian.ReadUInt32(data, entryOffset + 10, "InstrExt long repeat"));
                    extension.LongRepeatLength = checked((int)BigEndian.ReadUInt32(data, entryOffset + 14, "InstrExt long repeat length"));
                }
            }
        }

        private static void ParseInstrumentNames(MmdModule module, int offset, int entries, int entrySize)
        {
            var data = (ReadOnlySpan<byte>)module.Data;
            var count = Math.Min(entries, MmdConstants.MaxLegacyInstruments);
            for (var i = 0; i < count; i++)
            {
                var entryOffset = offset + i * entrySize;
                var length = Math.Min(40, entrySize);
                if (!BigEndian.HasRange(data, entryOffset, length))
                {
                    return;
                }

                module.Song.SampleInfos[i].Extension.Name = ReadNullTerminatedString(data, entryOffset, length);
            }
        }

        private static void InferLegacyLoopFlags(MmdModule module)
        {
            for (var i = 0; i < module.Song.SampleInfos.Length; i++)
            {
                if (module.Song.SampleInfos[i].RepeatLength > 2)
                {
                    module.Song.SampleInfos[i].Extension.Flags |= MmdConstants.InstrFlagLoop;
                }
            }
        }

        private static void ParseBlocks(MmdModule module)
        {
            var data = (ReadOnlySpan<byte>)module.Data;
            if (module.Song.NumBlocks <= 0)
            {
                throw new ModuleLoadException("The song contains no blocks.");
            }

            if (module.Song.BlockArrayOffset == 0)
            {
                throw new ModuleLoadException("The block array pointer is null.");
            }

            BigEndian.RequireRange(data, module.Song.BlockArrayOffset, module.Song.NumBlocks * 4, "block array");
            for (var i = 0; i < module.Song.NumBlocks; i++)
            {
                var blockPtr = BigEndian.ReadPointer(data, module.Song.BlockArrayOffset + i * 4, "block pointer");
                if (blockPtr == 0)
                {
                    throw new ModuleLoadException($"Block {i} has a null pointer.");
                }

                module.Blocks.Add(module.Version == MmdVersion.Mmd0
                    ? ParseMmd0Block(module, i, blockPtr)
                    : ParseMmd1Block(module, i, blockPtr));
            }
        }

        private static MmdBlock ParseMmd0Block(MmdModule module, int index, int offset)
        {
            var data = (ReadOnlySpan<byte>)module.Data;
            BigEndian.RequireRange(data, offset, 2, "MMD0 block header");
            var tracks = BigEndian.ReadByte(data, offset, "MMD0 block tracks");
            var lines = BigEndian.ReadByte(data, offset + 1, "MMD0 block lines") + 1;
            if (tracks <= 0)
            {
                throw new ModuleLoadException($"Block {index} has no tracks.");
            }

            var block = new MmdBlock(index, tracks, lines);
            var cursor = offset + 2;
            BigEndian.RequireRange(data, cursor, tracks * lines * 3, "MMD0 block data");
            for (var row = 0; row < lines; row++)
            {
                for (var track = 0; track < tracks; track++)
                {
                    var a = data[cursor++];
                    var b = data[cursor++];
                    var c = data[cursor++];
                    block.Cells[row, track] = new MmdCell
                    {
                        Note = (byte)(a & 0x3F),
                        Instrument = (byte)(((a & 0x80) >> 3) | ((a & 0x40) >> 1) | ((b & 0xF0) >> 4)),
                        Command = (byte)(b & 0x0F),
                        Data = c
                    };
                }
            }

            return block;
        }

        private static MmdBlock ParseMmd1Block(MmdModule module, int index, int offset)
        {
            var data = (ReadOnlySpan<byte>)module.Data;
            BigEndian.RequireRange(data, offset, 8, "MMD1 block header");
            var tracks = BigEndian.ReadUInt16(data, offset, "MMD1 block tracks");
            var lines = BigEndian.ReadUInt16(data, offset + 2, "MMD1 block lines") + 1;
            var infoPtr = BigEndian.ReadPointer(data, offset + 4, "MMD1 block info pointer");
            if (tracks <= 0)
            {
                throw new ModuleLoadException($"Block {index} has no tracks.");
            }

            var block = new MmdBlock(index, tracks, lines);
            var cursor = offset + 8;
            BigEndian.RequireRange(data, cursor, tracks * lines * 4, "MMD1 block data");
            for (var row = 0; row < lines; row++)
            {
                for (var track = 0; track < tracks; track++)
                {
                    var note = data[cursor++];
                    var instrument = data[cursor++];
                    var command = data[cursor++];
                    var commandData = data[cursor++];
                    block.Cells[row, track] = new MmdCell
                    {
                        Note = (byte)(note & 0x7F),
                        Instrument = (byte)(instrument & 0x3F),
                        Command = command,
                        Data = commandData
                    };
                }
            }

            if (infoPtr != 0)
            {
                ParseBlockInfo(module, block, infoPtr);
            }

            return block;
        }

        private static void ParseBlockInfo(MmdModule module, MmdBlock block, int infoPtr)
        {
            var data = (ReadOnlySpan<byte>)module.Data;
            if (!BigEndian.HasRange(data, infoPtr, 36))
            {
                module.Diagnostics.Add(new ModuleDiagnostic(ModuleDiagnosticSeverity.Warning, $"Block {block.Index} has an out-of-range BlockInfo pointer.", "MMD_BLOCKINFO_RANGE"));
                return;
            }

            var namePtr = BigEndian.ReadPointer(data, infoPtr + 4, "block name pointer");
            var nameLength = checked((int)BigEndian.ReadUInt32(data, infoPtr + 8, "block name length"));
            if (namePtr != 0 && nameLength > 0 && BigEndian.HasRange(data, namePtr, nameLength))
            {
                block.Name = ReadNullTerminatedString(data, namePtr, nameLength);
            }

            var pageTablePtr = BigEndian.ReadPointer(data, infoPtr + 12, "command page table pointer");
            if (pageTablePtr == 0 || !BigEndian.HasRange(data, pageTablePtr, 4))
            {
                return;
            }

            var pageCount = BigEndian.ReadUInt16(data, pageTablePtr, "command page count");
            if (pageCount <= 0)
            {
                return;
            }

            BigEndian.RequireRange(data, pageTablePtr + 4, pageCount * 4, "command page pointer table");
            for (var page = 0; page < pageCount; page++)
            {
                var pagePtr = BigEndian.ReadPointer(data, pageTablePtr + 4 + page * 4, "command page pointer");
                if (pagePtr == 0)
                {
                    continue;
                }

                var commandPage = new MmdCommandPage(block.TrackCount, block.LineCount);
                BigEndian.RequireRange(data, pagePtr, block.TrackCount * block.LineCount * 2, "command page data");
                var cursor = pagePtr;
                for (var row = 0; row < block.LineCount; row++)
                {
                    for (var track = 0; track < block.TrackCount; track++)
                    {
                        commandPage.Commands[row, track] = new MmdCommand
                        {
                            CommandNumber = data[cursor++],
                            Data = data[cursor++]
                        };
                    }
                }

                block.AdditionalCommandPages.Add(commandPage);
            }
        }

        private static void ParseInstruments(MmdModule module)
        {
            var song = module.Song;
            var sampleCount = Math.Min(Math.Max(song.NumSamples, 0), MmdConstants.MaxLegacyInstruments);
            if (song.SampleArrayOffset == 0)
            {
                module.Diagnostics.Add(new ModuleDiagnostic(ModuleDiagnosticSeverity.Warning, "The instrument pointer array is null; instruments are unavailable.", "MMD_NO_INSTRUMENTS"));
                AddEmptyInstruments(module, sampleCount);
                return;
            }

            var data = (ReadOnlySpan<byte>)module.Data;
            BigEndian.RequireRange(data, song.SampleArrayOffset, sampleCount * 4, "instrument pointer array");
            for (var i = 0; i < sampleCount; i++)
            {
                var pointer = BigEndian.ReadPointer(data, song.SampleArrayOffset + i * 4, "instrument pointer");
                module.Instruments.Add(ParseInstrument(module, i, pointer));
            }
        }

        private static void AddEmptyInstruments(MmdModule module, int count)
        {
            for (var i = 0; i < count; i++)
            {
                module.Instruments.Add(new MmdInstrument(i));
            }
        }

        private static MmdInstrument ParseInstrument(MmdModule module, int index, int pointer)
        {
            var instrument = new MmdInstrument(index)
            {
                Pointer = pointer
            };

            if (pointer == 0)
            {
                return instrument;
            }

            var data = (ReadOnlySpan<byte>)module.Data;
            if (!BigEndian.HasRange(data, pointer, 6))
            {
                module.Diagnostics.Add(new ModuleDiagnostic(ModuleDiagnosticSeverity.Warning, $"Instrument {index + 1} points outside the module.", "MMD_INSTRUMENT_RANGE"));
                instrument.Kind = MmdInstrumentKind.Unsupported;
                return instrument;
            }

            instrument.Length = BigEndian.ReadUInt32(data, pointer, "instrument length");
            instrument.RawType = BigEndian.ReadInt16(data, pointer + 4, "instrument type");
            var sampleInfo = module.Song.SampleInfos[index];
            var outputDevice = sampleInfo.Extension.OutputDevice;
            var repeatOffset = sampleInfo.Extension.LongRepeatOffset ?? sampleInfo.RepeatOffset;
            var repeatLength = sampleInfo.Extension.LongRepeatLength ?? sampleInfo.RepeatLength;
            if (UsesPaulaWordLengths(module, outputDevice))
            {
                repeatOffset = FloorToEvenByteCount(repeatOffset);
                repeatLength = FloorToEvenByteCount(repeatLength);
            }

            instrument.RepeatOffset = repeatOffset;
            instrument.RepeatLength = repeatLength;
            instrument.LoopEnabled = (sampleInfo.Extension.Flags & MmdConstants.InstrFlagLoop) != 0 && instrument.RepeatLength > 2;

            if (instrument.RawType == -1 || instrument.RawType == -2)
            {
                ParseSynthInstrument(module, instrument, pointer);
            }
            else if (instrument.RawType >= 0)
            {
                ParseSampleInstrument(module, instrument, pointer + 6, checked((int)instrument.Length), instrument.RawType, outputDevice);
            }
            else
            {
                instrument.Kind = MmdInstrumentKind.Unsupported;
                module.Diagnostics.Add(new ModuleDiagnostic(ModuleDiagnosticSeverity.Warning, $"Instrument {index + 1} has unsupported type {instrument.RawType}.", "MMD_INSTRUMENT_TYPE"));
            }

            return instrument;
        }

        private static void ParseSampleInstrument(MmdModule module, MmdInstrument instrument, int dataOffset, int length, short rawType, byte outputDevice)
        {
            instrument.Kind = MmdInstrumentKind.Sample;
            instrument.Is16Bit = ShouldDecodeAs16Bit(module, rawType, outputDevice);
            instrument.IsStereo = (rawType & 0x20) != 0;
            instrument.TypeCode = DecodeSampleType(rawType);

            var channelLength = Math.Max(length, 0);
            if (UsesPaulaWordLengths(module, outputDevice))
            {
                // MMD stores InstrHdr.length in bytes. Paula's AUDxLEN is in
                // words, so standard Amiga playback can only consume complete
                // two-byte pairs from that 8-bit PCM stream.
                channelLength = FloorToEvenByteCount(channelLength);
            }

            var totalLength = instrument.IsStereo ? checked(channelLength * 2) : channelLength;
            var data = (ReadOnlySpan<byte>)module.Data;
            if (!BigEndian.HasRange(data, dataOffset, totalLength))
            {
                module.Diagnostics.Add(new ModuleDiagnostic(ModuleDiagnosticSeverity.Warning, $"Instrument {instrument.Index + 1} sample data is truncated.", "MMD_SAMPLE_RANGE"));
                totalLength = Math.Max(0, module.Data.Length - dataOffset);
                channelLength = instrument.IsStereo ? totalLength / 2 : totalLength;
            }

            instrument.LeftSamples = DecodeSampleChannel(module.Data, dataOffset, channelLength, instrument.Is16Bit);
            instrument.RightSamples = instrument.IsStereo
                ? DecodeSampleChannel(module.Data, dataOffset + channelLength, channelLength, instrument.Is16Bit)
                : instrument.LeftSamples;
        }

        private static void ParseSynthInstrument(MmdModule module, MmdInstrument instrument, int pointer)
        {
            var data = (ReadOnlySpan<byte>)module.Data;
            instrument.Kind = instrument.RawType == -2 ? MmdInstrumentKind.Hybrid : MmdInstrumentKind.Synth;
            BigEndian.RequireRange(data, pointer, 278, "synth instrument");
            instrument.DefaultDecay = BigEndian.ReadByte(data, pointer + 6, "synth default decay");
            instrument.SynthRepeatOffset = BigEndian.ReadUInt16(data, pointer + 10, "synth repeat") * 2;
            instrument.SynthRepeatLength = BigEndian.ReadUInt16(data, pointer + 12, "synth repeat length") * 2;
            instrument.VolumeSequenceLength = BigEndian.ReadUInt16(data, pointer + 14, "synth volume table length");
            instrument.WaveformSequenceLength = BigEndian.ReadUInt16(data, pointer + 16, "synth waveform table length");
            instrument.VolumeSpeed = BigEndian.ReadByte(data, pointer + 18, "synth volume speed");
            instrument.WaveformSpeed = BigEndian.ReadByte(data, pointer + 19, "synth waveform speed");
            var waveformCount = BigEndian.ReadUInt16(data, pointer + 20, "synth waveform count");

            instrument.VolumeSequence = CopyBytes(module.Data, pointer + 22, Math.Min(instrument.VolumeSequenceLength, 128));
            instrument.WaveformSequence = CopyBytes(module.Data, pointer + 150, Math.Min(instrument.WaveformSequenceLength, 128));

            var pointerTableOffset = pointer + 278;
            if (!BigEndian.HasRange(data, pointerTableOffset, waveformCount * 4))
            {
                module.Diagnostics.Add(new ModuleDiagnostic(ModuleDiagnosticSeverity.Warning, $"Instrument {instrument.Index + 1} waveform pointer table is truncated.", "MMD_SYNTH_WF_RANGE"));
                waveformCount = (ushort)Math.Max(0, (module.Data.Length - pointerTableOffset) / 4);
            }

            for (var i = 0; i < waveformCount; i++)
            {
                var relative = BigEndian.ReadPointer(data, pointerTableOffset + i * 4, "synth waveform pointer");
                if (relative == 0)
                {
                    instrument.Waveforms.Add(new MmdSynthWaveform(i, Array.Empty<float>(), Array.Empty<sbyte>()));
                    continue;
                }

                var waveformOffset = pointer + relative;
                if (instrument.Kind == MmdInstrumentKind.Hybrid && i == 0)
                {
                    if (BigEndian.HasRange(data, waveformOffset, 6))
                    {
                        var length = checked((int)BigEndian.ReadUInt32(data, waveformOffset, "hybrid sample length"));
                        var rawType = BigEndian.ReadInt16(data, waveformOffset + 4, "hybrid sample type");
                        var hybrid = new MmdInstrument(instrument.Index)
                        {
                            RawType = rawType,
                            RepeatOffset = instrument.SynthRepeatOffset,
                            RepeatLength = instrument.SynthRepeatLength,
                            LoopEnabled = instrument.SynthRepeatLength > 2
                        };
                        ParseSampleInstrument(module, hybrid, waveformOffset + 6, length, rawType, MmdConstants.OutputStd);
                        instrument.LeftSamples = hybrid.LeftSamples;
                        instrument.RightSamples = hybrid.RightSamples;
                    }

                    continue;
                }

                instrument.Waveforms.Add(ParseWaveform(module, i, waveformOffset));
            }
        }

        private static MmdSynthWaveform ParseWaveform(MmdModule module, int index, int offset)
        {
            var data = (ReadOnlySpan<byte>)module.Data;
            if (!BigEndian.HasRange(data, offset, 2))
            {
                module.Diagnostics.Add(new ModuleDiagnostic(ModuleDiagnosticSeverity.Warning, $"Waveform {index} points outside the module.", "MMD_WAVEFORM_RANGE"));
                return new MmdSynthWaveform(index, Array.Empty<float>(), Array.Empty<sbyte>());
            }

            var lengthWords = BigEndian.ReadUInt16(data, offset, "waveform length");
            var lengthBytes = lengthWords * 2;
            if (!BigEndian.HasRange(data, offset + 2, lengthBytes))
            {
                module.Diagnostics.Add(new ModuleDiagnostic(ModuleDiagnosticSeverity.Warning, $"Waveform {index} data is truncated.", "MMD_WAVEFORM_DATA_RANGE"));
                lengthBytes = Math.Max(0, module.Data.Length - offset - 2);
            }

            var sampleDataOffset = offset + 2;
            return new MmdSynthWaveform(
                index,
                DecodeSigned8(module.Data, sampleDataOffset, lengthBytes),
                DecodeSigned8Raw(module.Data, sampleDataOffset, 128));
        }

        private static float[] DecodeSampleChannel(byte[] data, int offset, int byteLength, bool is16Bit)
        {
            if (byteLength <= 0 || offset < 0 || offset >= data.Length)
            {
                return Array.Empty<float>();
            }

            byteLength = Math.Min(byteLength, data.Length - offset);
            if (!is16Bit)
            {
                return DecodeSigned8(data, offset, byteLength);
            }

            var sampleCount = byteLength / 2;
            var samples = new float[sampleCount];
            for (var i = 0; i < sampleCount; i++)
            {
                var value = (short)((data[offset + i * 2] << 8) | data[offset + i * 2 + 1]);
                samples[i] = value / 32768.0f;
            }

            return samples;
        }

        private static bool ShouldDecodeAs16Bit(MmdModule module, short rawType, byte outputDevice)
        {
            if ((rawType & 0x10) == 0)
            {
                return false;
            }

            // Standard Paula samples are signed 8-bit PCM. The S_16 flag belongs
            // to later mixed/output-device paths; applying it to legacy Amiga
            // playback turns ordinary sample bytes into bogus 16-bit words.
            return module.UsesMixingMode || outputDevice != MmdConstants.OutputStd;
        }

        private static int DecodeSampleType(short rawType)
        {
            if ((rawType & 0x1F) == 0x18)
            {
                return 0;
            }

            return rawType & 0x0F;
        }

        private static bool UsesPaulaWordLengths(MmdModule module, byte outputDevice)
        {
            return !module.UsesMixingMode && outputDevice == MmdConstants.OutputStd;
        }

        private static int FloorToEvenByteCount(int byteCount)
        {
            return Math.Max(0, byteCount) & ~1;
        }

        private static float[] DecodeSigned8(byte[] data, int offset, int byteLength)
        {
            if (byteLength <= 0 || offset < 0 || offset >= data.Length)
            {
                return Array.Empty<float>();
            }

            byteLength = Math.Min(byteLength, data.Length - offset);
            var samples = new float[byteLength];
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = unchecked((sbyte)data[offset + i]) / 128.0f;
            }

            return samples;
        }

        private static sbyte[] DecodeSigned8Raw(byte[] data, int offset, int byteLength)
        {
            if (byteLength <= 0 || offset < 0 || offset >= data.Length)
            {
                return Array.Empty<sbyte>();
            }

            byteLength = Math.Min(byteLength, data.Length - offset);
            var samples = new sbyte[byteLength];
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = unchecked((sbyte)data[offset + i]);
            }

            return samples;
        }

        private static byte[] CopyBytes(byte[] data, int offset, int length)
        {
            if (length <= 0 || offset < 0 || offset >= data.Length)
            {
                return Array.Empty<byte>();
            }

            length = Math.Min(length, data.Length - offset);
            var copy = new byte[length];
            Array.Copy(data, offset, copy, 0, length);
            return copy;
        }

        private static void EnsureTrackDefaults(MmdModule module)
        {
            var maxBlockTracks = 0;
            foreach (var block in module.Blocks)
            {
                maxBlockTracks = Math.Max(maxBlockTracks, block.TrackCount);
            }

            module.Song.NumTracks = Math.Max(module.Song.NumTracks, Math.Max(maxBlockTracks, 4));
            if (module.Song.TrackVolumes.Length < module.Song.NumTracks)
            {
                var volumes = new byte[module.Song.NumTracks];
                for (var i = 0; i < volumes.Length; i++)
                {
                    volumes[i] = i < module.Song.TrackVolumes.Length && module.Song.TrackVolumes[i] != 0
                        ? module.Song.TrackVolumes[i]
                        : (byte)64;
                }

                module.Song.TrackVolumes = volumes;
            }
            else
            {
                for (var i = 0; i < module.Song.TrackVolumes.Length; i++)
                {
                    if (module.Song.TrackVolumes[i] == 0)
                    {
                        module.Song.TrackVolumes[i] = 64;
                    }
                }
            }

            if (module.Song.TrackPans.Length < module.Song.NumTracks)
            {
                var pans = new sbyte[module.Song.NumTracks];
                for (var i = 0; i < pans.Length; i++)
                {
                    pans[i] = i < module.Song.TrackPans.Length ? module.Song.TrackPans[i] : GetDefaultPan(i);
                }

                module.Song.TrackPans = pans;
            }
        }

        private static sbyte GetDefaultPan(int track)
        {
            switch (track & 3)
            {
                case 0:
                case 3:
                    return -16;
                default:
                    return 16;
            }
        }

        private static string ReadFixedString(ReadOnlySpan<byte> data, int offset, int length)
        {
            BigEndian.RequireRange(data, offset, length, "fixed string");
            return ReadNullTerminatedString(data, offset, length);
        }

        private static string ReadNullTerminatedString(ReadOnlySpan<byte> data, int offset, int length)
        {
            BigEndian.RequireRange(data, offset, length, "string");
            var actualLength = 0;
            while (actualLength < length && data[offset + actualLength] != 0)
            {
                actualLength++;
            }

            return Encoding.ASCII.GetString(data.Slice(offset, actualLength).ToArray());
        }
    }
}
