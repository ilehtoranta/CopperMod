using System;
using System.Collections.Generic;
using System.Text;
using CopperMod.Abstractions;

namespace CopperMod.Sid
{
    internal static class SidParser
    {
        public static bool Identify(ReadOnlySpan<byte> data)
        {
            if (data.Length < 4)
            {
                return false;
            }

            var magic = BigEndian.ReadUInt32(data, 0, "magic");
            return magic == SidConstants.PsidMagic || magic == SidConstants.RsidMagic;
        }

        public static SidModule Parse(ReadOnlySpan<byte> data)
        {
            if (data.Length < SidConstants.V1HeaderLength)
            {
                throw new ModuleLoadException("SID data is shorter than the minimum PSID header.");
            }

            var diagnostics = new List<ModuleDiagnostic>();
            var magic = BigEndian.ReadUInt32(data, 0, "magic");
            var kind = magic switch
            {
                SidConstants.PsidMagic => SidFileKind.Psid,
                SidConstants.RsidMagic => SidFileKind.Rsid,
                _ => throw new UnsupportedModuleFormatException("The data is not a PSID or RSID file.")
            };

            var version = BigEndian.ReadUInt16(data, 4, "version");
            if (version < 1 || version > 4)
            {
                throw new UnsupportedModuleFormatException($"Unsupported SID header version {version}.");
            }

            if (kind == SidFileKind.Rsid && (version < 2 || version > 3))
            {
                throw new UnsupportedModuleFormatException("RSID version must be 2 or 3.");
            }

            var dataOffset = BigEndian.ReadUInt16(data, 6, "dataOffset");
            if (dataOffset < SidConstants.V1HeaderLength || dataOffset > data.Length)
            {
                throw new ModuleLoadException("SID data offset is outside the file.");
            }

            var loadAddress = BigEndian.ReadUInt16(data, 8, "loadAddress");
            var initAddress = BigEndian.ReadUInt16(data, 0x0A, "initAddress");
            var playAddress = BigEndian.ReadUInt16(data, 0x0C, "playAddress");
            var songs = BigEndian.ReadUInt16(data, 0x0E, "songs");
            var startSong = BigEndian.ReadUInt16(data, 0x10, "startSong");
            var speed = BigEndian.ReadUInt32(data, 0x12, "speed");
            var title = ReadFixedString(data, 0x16, 32);
            var author = ReadFixedString(data, 0x36, 32);
            var released = ReadFixedString(data, 0x56, 32);
            ushort flags = 0;
            byte relocationStartPage = 0;
            byte relocationPageLength = 0;
            SidClock clock = SidClock.Unknown;
            SidChipModel chipModel = SidChipModel.Unknown;
            if (version >= 2)
            {
                if (data.Length < SidConstants.V2HeaderLength)
                {
                    throw new ModuleLoadException("SID data is shorter than the declared v2+ header.");
                }

                flags = BigEndian.ReadUInt16(data, 0x76, "flags");
                relocationStartPage = data[0x78];
                relocationPageLength = data[0x79];
                _ = BigEndian.ReadUInt16(data, 0x7A, "reserved");
                clock = DecodeClock(flags);
                chipModel = DecodeChipModel(flags);
            }

            if (songs == 0 || songs > SidConstants.MaxSubSongs)
            {
                throw new ModuleLoadException("SID song count must be in the range 1..256.");
            }

            var defaultIndex = startSong == 0 ? 0 : startSong - 1;
            if (defaultIndex >= songs)
            {
                diagnostics.Add(new ModuleDiagnostic(
                    ModuleDiagnosticSeverity.Warning,
                    "SID default subtune is outside the advertised song count; using subtune 1.",
                    "SID_STARTSONG_RANGE"));
                defaultIndex = 0;
            }

            var payloadOffset = dataOffset;
            ushort effectiveLoadAddress;
            if (loadAddress == 0)
            {
                if (payloadOffset + 2 > data.Length)
                {
                    throw new ModuleLoadException("SID payload does not contain an embedded load address.");
                }

                effectiveLoadAddress = (ushort)(data[payloadOffset] | (data[payloadOffset + 1] << 8));
                payloadOffset += 2;
            }
            else
            {
                effectiveLoadAddress = loadAddress;
            }

            if (payloadOffset > data.Length)
            {
                throw new ModuleLoadException("SID payload offset is outside the file.");
            }

            var payload = data.Slice(payloadOffset).ToArray();
            ValidateLoadRange(effectiveLoadAddress, payload.Length);
            ValidateRsidRules(kind, version, loadAddress, effectiveLoadAddress, initAddress, playAddress, speed, flags);
            ValidateRelocation(kind, relocationStartPage, relocationPageLength, effectiveLoadAddress, payload.Length, diagnostics);
            var isBasicRsid = kind == SidFileKind.Rsid && IsRsidBasicFlagSet(flags);
            if (isBasicRsid)
            {
                diagnostics.Add(new ModuleDiagnostic(
                    ModuleDiagnosticSeverity.Info,
                    "RSID BASIC program will be executed by CopperMod's native BASIC runner.",
                    "SID_RSID_BASIC_NATIVE_RUNNER"));
            }

            var chips = ParseChips(data, version, dataOffset, diagnostics);
            if (chips.Count == 0)
            {
                chips.Add(new SidChipPlacement(0, SidConstants.DefaultSidBaseAddress));
            }

            if (chipModel == SidChipModel.Unknown)
            {
                diagnostics.Add(new ModuleDiagnostic(
                    ModuleDiagnosticSeverity.Info,
                    "SID chip model is unknown; defaulting to MOS6581.",
                    "SID_CHIP_DEFAULT"));
            }

            if (clock == SidClock.Unknown)
            {
                diagnostics.Add(new ModuleDiagnostic(
                    ModuleDiagnosticSeverity.Info,
                    "SID clock is unknown; defaulting to PAL.",
                    "SID_CLOCK_DEFAULT"));
            }

            return new SidModule(
                kind,
                version,
                dataOffset,
                loadAddress,
                effectiveLoadAddress,
                isBasicRsid ? initAddress : initAddress == 0 ? effectiveLoadAddress : initAddress,
                playAddress,
                songs,
                defaultIndex,
                speed,
                flags,
                relocationStartPage,
                relocationPageLength,
                title,
                author,
                released,
                clock,
                chipModel,
                chips,
                payload,
                diagnostics);
        }

        private static string ReadFixedString(ReadOnlySpan<byte> data, int offset, int length)
        {
            var span = data.Slice(offset, length);
            var zero = span.IndexOf((byte)0);
            if (zero >= 0)
            {
                span = span.Slice(0, zero);
            }

            return Encoding.Latin1.GetString(span).TrimEnd();
        }

        private static SidClock DecodeClock(ushort flags)
        {
            return ((flags >> 2) & 0x03) switch
            {
                1 => SidClock.Pal,
                2 => SidClock.Ntsc,
                3 => SidClock.PalAndNtsc,
                _ => SidClock.Unknown
            };
        }

        private static SidChipModel DecodeChipModel(ushort flags)
        {
            return ((flags >> 4) & 0x03) switch
            {
                1 => SidChipModel.Mos6581,
                2 => SidChipModel.Mos8580,
                3 => SidChipModel.Any,
                _ => SidChipModel.Unknown
            };
        }

        private static List<SidChipPlacement> ParseChips(
            ReadOnlySpan<byte> data,
            int version,
            int dataOffset,
            List<ModuleDiagnostic> diagnostics)
        {
            var chips = new List<SidChipPlacement> { new SidChipPlacement(0, SidConstants.DefaultSidBaseAddress) };
            if (version < 3 || dataOffset < 0x80 || data.Length < 0x80)
            {
                return chips;
            }

            AddExtraChip(data[0x7E], 1, chips, diagnostics);
            AddExtraChip(data[0x7F], 2, chips, diagnostics);
            return chips;
        }

        private static void AddExtraChip(byte encodedAddress, int index, List<SidChipPlacement> chips, List<ModuleDiagnostic> diagnostics)
        {
            if (encodedAddress == 0)
            {
                return;
            }

            var baseAddress = DecodeExtraSidAddress(encodedAddress);
            if (!IsValidSidBaseAddress(baseAddress))
            {
                diagnostics.Add(new ModuleDiagnostic(
                    ModuleDiagnosticSeverity.Warning,
                    $"SID chip {index + 1} has unsupported base address ${baseAddress:X4} and was ignored.",
                    "SID_EXTRA_BASE"));
                return;
            }

            chips.Add(new SidChipPlacement(index, baseAddress));
        }

        private static ushort DecodeExtraSidAddress(byte encodedAddress)
        {
            return (ushort)(0xD000 | (encodedAddress << 4));
        }

        private static bool IsValidSidBaseAddress(ushort address)
        {
            return address >= 0xD400 &&
                address <= 0xDFE0 &&
                (address & 0x001F) == 0 &&
                (address < 0xD800 || address >= 0xDE00);
        }

        private static void ValidateLoadRange(ushort address, int length)
        {
            if (length < 0 || address + length > 0x10000)
            {
                throw new ModuleLoadException("SID payload does not fit in C64 memory.");
            }
        }

        private static void ValidateRsidRules(
            SidFileKind kind,
            int version,
            ushort loadAddress,
            ushort effectiveLoadAddress,
            ushort initAddress,
            ushort playAddress,
            uint speed,
            ushort flags)
        {
            if (kind != SidFileKind.Rsid)
            {
                return;
            }

            if (version < 2 || loadAddress != 0 || playAddress != 0 || speed != 0)
            {
                throw new UnsupportedModuleFormatException("RSID fields do not satisfy Real C64 SID restrictions.");
            }

            var isBasicRsid = IsRsidBasicFlagSet(flags);
            if (effectiveLoadAddress < (isBasicRsid ? 0x0801 : 0x07E8))
            {
                throw new UnsupportedModuleFormatException("RSID payload load address must not be below $07E8.");
            }

            if (isBasicRsid)
            {
                if (initAddress != 0)
                {
                    throw new UnsupportedModuleFormatException("RSID BASIC programs must declare init address $0000.");
                }

                return;
            }

            if (initAddress < 0x07E8 || IsRomOrIoAddress(initAddress))
            {
                throw new UnsupportedModuleFormatException("RSID init address must point to RAM outside ROM/IO areas.");
            }
        }

        private static bool IsRsidBasicFlagSet(ushort flags)
        {
            return ((flags >> 1) & 1) != 0;
        }

        private static bool IsRomOrIoAddress(ushort address)
        {
            return (address >= 0xA000 && address <= 0xBFFF) || address >= 0xD000;
        }

        private static void ValidateRelocation(
            SidFileKind kind,
            byte startPage,
            byte pageLength,
            ushort loadAddress,
            int payloadLength,
            List<ModuleDiagnostic> diagnostics)
        {
            if ((startPage == 0 || startPage == 0xFF) && pageLength != 0)
            {
                diagnostics.Add(new ModuleDiagnostic(
                    ModuleDiagnosticSeverity.Warning,
                    "SID relocation page length is nonzero although relocation is disabled.",
                    "SID_RELOC_LENGTH"));
            }

            if (startPage == 0 || startPage == 0xFF || pageLength == 0)
            {
                return;
            }

            var relocationStart = startPage << 8;
            var relocationEnd = relocationStart + (pageLength << 8) - 1;
            var loadEnd = loadAddress + payloadLength - 1;
            if (RangesOverlap(relocationStart, relocationEnd, loadAddress, loadEnd))
            {
                diagnostics.Add(new ModuleDiagnostic(
                    ModuleDiagnosticSeverity.Warning,
                    "SID relocation range overlaps the payload load range.",
                    "SID_RELOC_OVERLAP"));
            }

            if (kind == SidFileKind.Rsid &&
                (relocationStart < 0x0400 || RangesOverlap(relocationStart, relocationEnd, 0xA000, 0xBFFF) || RangesOverlap(relocationStart, relocationEnd, 0xD000, 0xFFFF)))
            {
                diagnostics.Add(new ModuleDiagnostic(
                    ModuleDiagnosticSeverity.Warning,
                    "RSID relocation range overlaps reserved memory.",
                    "SID_RELOC_RSID"));
            }
        }

        private static bool RangesOverlap(int startA, int endA, int startB, int endB)
        {
            return startA <= endB && startB <= endA;
        }
    }
}
