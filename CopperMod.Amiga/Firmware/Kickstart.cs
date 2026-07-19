/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections.Generic;

namespace CopperMod.Amiga.Firmware
{
    internal enum KickstartBackendKind
    {
        HostShim,
        RomImage
    }

    internal enum KickstartVersion
    {
        Kickstart13,
        Kickstart20,
        Kickstart30,
        Kickstart31
    }

    internal sealed class KickstartConfiguration
    {
        private KickstartConfiguration(
            KickstartBackendKind backend,
            KickstartVersion version,
            byte[]? romImage)
        {
            Backend = backend;
            Version = version;
            RomImage = romImage ?? Array.Empty<byte>();
        }

        public static KickstartConfiguration HostShim13 { get; } = new KickstartConfiguration(
            KickstartBackendKind.HostShim,
            KickstartVersion.Kickstart13,
            null);

        public static KickstartConfiguration HostShim20 { get; } = new KickstartConfiguration(
            KickstartBackendKind.HostShim,
            KickstartVersion.Kickstart20,
            null);

        public KickstartBackendKind Backend { get; }

        public KickstartVersion Version { get; }

        public ReadOnlyMemory<byte> RomImage { get; }

        public string Description => $"{(Backend == KickstartBackendKind.HostShim ? "HostShim" : "ROM")} Kickstart {Version switch
        {
            KickstartVersion.Kickstart13 => "1.3",
            KickstartVersion.Kickstart20 => "2.0",
            KickstartVersion.Kickstart30 => "3.0",
            KickstartVersion.Kickstart31 => "3.1",
            _ => throw new ArgumentOutOfRangeException(nameof(Version))
        }}";

        public static KickstartConfiguration FromRomImage(
            KickstartVersion version,
            ReadOnlySpan<byte> romImage)
        {
            if (romImage.IsEmpty)
            {
                throw new ArgumentException("A Kickstart ROM image is required.", nameof(romImage));
            }

            return new KickstartConfiguration(
                KickstartBackendKind.RomImage,
                version,
                romImage.ToArray());
        }
    }

    internal sealed class KickstartTrapTable
    {
        public KickstartTrapTable(
            uint okCallbackAddress,
            Action<M68kCpuState> nullCallback,
            Action<M68kCpuState> ok,
            Action<M68kCpuState> openLibrary,
            Action<M68kCpuState> allocMem,
            Action<M68kCpuState> allocMemAndStore,
            Action<M68kCpuState> freeMem,
            Action<M68kCpuState> causeInterrupt,
            Action<M68kCpuState> addInterrupt,
            Action<M68kCpuState> removeInterrupt,
            Action<M68kCpuState> ableIcr,
            Action<M68kCpuState> setIcr,
            Action<M68kCpuState> dosOpen,
            Action<M68kCpuState> dosClose,
            Action<M68kCpuState> dosRead,
            Action<M68kCpuState> dosSeek)
        {
            OkCallbackAddress = okCallbackAddress;
            NullCallback = nullCallback ?? throw new ArgumentNullException(nameof(nullCallback));
            Ok = ok ?? throw new ArgumentNullException(nameof(ok));
            OpenLibrary = openLibrary ?? throw new ArgumentNullException(nameof(openLibrary));
            AllocMem = allocMem ?? throw new ArgumentNullException(nameof(allocMem));
            AllocMemAndStore = allocMemAndStore ?? throw new ArgumentNullException(nameof(allocMemAndStore));
            FreeMem = freeMem ?? throw new ArgumentNullException(nameof(freeMem));
            CauseInterrupt = causeInterrupt ?? throw new ArgumentNullException(nameof(causeInterrupt));
            AddInterrupt = addInterrupt ?? throw new ArgumentNullException(nameof(addInterrupt));
            RemoveInterrupt = removeInterrupt ?? throw new ArgumentNullException(nameof(removeInterrupt));
            AbleIcr = ableIcr ?? throw new ArgumentNullException(nameof(ableIcr));
            SetIcr = setIcr ?? throw new ArgumentNullException(nameof(setIcr));
            DosOpen = dosOpen ?? throw new ArgumentNullException(nameof(dosOpen));
            DosClose = dosClose ?? throw new ArgumentNullException(nameof(dosClose));
            DosRead = dosRead ?? throw new ArgumentNullException(nameof(dosRead));
            DosSeek = dosSeek ?? throw new ArgumentNullException(nameof(dosSeek));
        }

        public uint OkCallbackAddress { get; }

        public Action<M68kCpuState> NullCallback { get; }

        public Action<M68kCpuState> Ok { get; }

        public Action<M68kCpuState> OpenLibrary { get; }

        public Action<M68kCpuState> AllocMem { get; }

        public Action<M68kCpuState> AllocMemAndStore { get; }

        public Action<M68kCpuState> FreeMem { get; }

        public Action<M68kCpuState> CauseInterrupt { get; }

        public Action<M68kCpuState> AddInterrupt { get; }

        public Action<M68kCpuState> RemoveInterrupt { get; }

        public Action<M68kCpuState> AbleIcr { get; }

        public Action<M68kCpuState> SetIcr { get; }

        public Action<M68kCpuState> DosOpen { get; }

        public Action<M68kCpuState> DosClose { get; }

        public Action<M68kCpuState> DosRead { get; }

        public Action<M68kCpuState> DosSeek { get; }
    }

    internal sealed class AmigaKickstartHost
    {
        public const uint ExecLibraryBase = 0x00F1_0000;
        public const uint DosLibraryBase = 0x00F2_0000;
        public const uint CiaBResourceBase = 0x00F3_0000;
        public const uint ReqLibraryBase = 0x00F4_0000;
        public const uint DummyLibraryBase = 0x00F5_0000;
        public const uint CiaAResourceBase = 0x00F6_0000;
        public const uint IconLibraryBase = 0x00F7_0000;
        public const uint WorkbenchLibraryBase = 0x00F8_8000;
        public const uint GraphicsLibraryBase = 0x00F9_0000;
        public const uint IntuitionLibraryBase = 0x00FA_0000;
        public const uint ExpansionLibraryBase = 0x00FB_0000;
        public const uint ExecStructAddress = 0x0000_2000;
        public const uint HostPathBufferAddress = 0x0000_3000;
        public const int HostPathBufferLength = 512;

        private const uint KickstartRomEndAddress = 0x0100_0000;
        private readonly KickstartConfiguration _configuration;

        public AmigaKickstartHost(KickstartConfiguration? configuration = null)
        {
            _configuration = configuration ?? KickstartConfiguration.HostShim13;
        }

        public KickstartConfiguration Configuration => _configuration;

        public void Install(AmigaBus bus, KickstartTrapTable traps)
        {
            ArgumentNullException.ThrowIfNull(bus);
            ArgumentNullException.ThrowIfNull(traps);

            if (_configuration.Backend == KickstartBackendKind.RomImage)
            {
                InstallRomImage(bus);
                return;
            }

            InstallHostShim(bus, traps);
        }

        public void InstallHostShim(AmigaBus bus, KickstartTrapTable traps)
        {
            ArgumentNullException.ThrowIfNull(bus);
            ArgumentNullException.ThrowIfNull(traps);

            bus.MapReadOnlyMemory(AmigaKickstartRomFont.BaseAddress, AmigaKickstartRomFont.CreateTopazCompatibleFont());
            bus.WriteLong(0, ExecStructAddress);
            bus.WriteLong(4, ExecLibraryBase);
            if (traps.OkCallbackAddress != 0)
            {
                bus.WriteLong(ExecStructAddress + 0x68, traps.OkCallbackAddress);
            }

            RegisterExecLibrary(bus, traps);
            RegisterDosLibrary(bus, traps);
            RegisterCiaResource(bus, traps);
            RegisterReqLibrary(bus, traps);
            RegisterIntuitionLibrary(bus, traps);
            RegisterDummyLibrary(bus, traps);
        }

        public void InstallRomImage(AmigaBus bus)
        {
            ArgumentNullException.ThrowIfNull(bus);
            if (_configuration.Backend != KickstartBackendKind.RomImage)
            {
                throw new InvalidOperationException("InstallRomImage requires a ROM-backed Kickstart configuration.");
            }

            var image = _configuration.RomImage;
            if (image.IsEmpty)
            {
                throw new InvalidOperationException("A Kickstart ROM configuration must include ROM bytes.");
            }

            if (image.Length > KickstartRomEndAddress)
            {
                throw new InvalidOperationException("The Kickstart ROM image is too large for the emulated ROM address window.");
            }

            bus.MapReadOnlyMemory(KickstartRomEndAddress - (uint)image.Length, image.Span);
        }

        private static void RegisterExecLibrary(AmigaBus bus, KickstartTrapTable traps)
        {
            RegisterLibraryCallback(bus, ExecLibraryBase, -408, traps.OpenLibrary);
            RegisterLibraryCallback(bus, ExecLibraryBase, -498, traps.OpenLibrary);
            RegisterLibraryCallback(bus, ExecLibraryBase, -414, traps.Ok);
            RegisterLibraryCallback(bus, ExecLibraryBase, -198, traps.AllocMem);
            RegisterLibraryCallback(bus, ExecLibraryBase, -210, traps.FreeMem);
            RegisterLibraryCallback(bus, ExecLibraryBase, -180, traps.CauseInterrupt);
            RegisterLibraryCallback(bus, ExecLibraryBase, -174, traps.RemoveInterrupt);
            RegisterLibraryCallback(bus, ExecLibraryBase, -168, traps.AddInterrupt);
            RegisterLibraryCallback(bus, ExecLibraryBase, -162, traps.AddInterrupt);
            RegisterLibraryCallback(bus, ExecLibraryBase, -396, traps.AllocMemAndStore);
        }

        private static void RegisterDosLibrary(AmigaBus bus, KickstartTrapTable traps)
        {
            RegisterLibraryCallback(bus, DosLibraryBase, -30, traps.DosOpen);
            RegisterLibraryCallback(bus, DosLibraryBase, -36, traps.DosClose);
            RegisterLibraryCallback(bus, DosLibraryBase, -42, traps.DosRead);
            RegisterLibraryCallback(bus, DosLibraryBase, -66, traps.DosSeek);
            RegisterLibraryCallback(bus, DosLibraryBase, -174, traps.Ok);
            RegisterLibraryCallback(bus, DosLibraryBase, -180, traps.Ok);
            RegisterLibraryCallback(bus, DosLibraryBase, -396, traps.Ok);
            RegisterLibraryCallback(bus, DosLibraryBase, -408, traps.OpenLibrary);
        }

        private static void RegisterCiaResource(AmigaBus bus, KickstartTrapTable traps)
        {
            RegisterCiaResource(bus, CiaAResourceBase, traps);
            RegisterCiaResource(bus, CiaBResourceBase, traps);
        }

        private static void RegisterCiaResource(AmigaBus bus, uint resourceBase, KickstartTrapTable traps)
        {
            RegisterLibraryCallback(bus, resourceBase, -6, traps.AddInterrupt);
            RegisterLibraryCallback(bus, resourceBase, -12, traps.RemoveInterrupt);
            RegisterLibraryCallback(bus, resourceBase, -18, traps.AbleIcr);
            RegisterLibraryCallback(bus, resourceBase, -24, traps.SetIcr);
        }

        private static void RegisterReqLibrary(AmigaBus bus, KickstartTrapTable traps)
        {
            RegisterLibraryCallback(bus, ReqLibraryBase, -6, traps.Ok);
            RegisterLibraryCallback(bus, ReqLibraryBase, -12, traps.Ok);
            RegisterLibraryCallback(bus, ReqLibraryBase, -174, traps.Ok);
            RegisterLibraryCallback(bus, ReqLibraryBase, -180, traps.Ok);
            RegisterLibraryCallback(bus, ReqLibraryBase, -396, traps.Ok);
            RegisterLibraryCallback(bus, ReqLibraryBase, -408, traps.OpenLibrary);
        }

        private static void RegisterIntuitionLibrary(AmigaBus bus, KickstartTrapTable traps)
        {
            RegisterLibraryCallback(bus, IntuitionLibraryBase, -396, traps.AllocMemAndStore);
            RegisterLibraryCallback(bus, IntuitionLibraryBase, -408, traps.Ok);
        }

        private static void RegisterDummyLibrary(AmigaBus bus, KickstartTrapTable traps)
        {
            RegisterLibraryCallback(bus, DummyLibraryBase, -6, traps.Ok);
            RegisterLibraryCallback(bus, DummyLibraryBase, -12, traps.Ok);
            RegisterLibraryCallback(bus, DummyLibraryBase, -174, traps.Ok);
            RegisterLibraryCallback(bus, DummyLibraryBase, -180, traps.Ok);
            RegisterLibraryCallback(bus, DummyLibraryBase, -396, traps.Ok);
            RegisterLibraryCallback(bus, DummyLibraryBase, -408, traps.OpenLibrary);
        }

        private static void RegisterLibraryCallback(
            AmigaBus bus,
            uint libraryBase,
            int displacement,
            Action<M68kCpuState> callback)
        {
            bus.RegisterHostTrapStub(unchecked((uint)((int)libraryBase + displacement)), callback);
        }
    }

    internal static class AmigaKickstartRomFont
    {
        public const uint BaseAddress = 0x00FC_0000;
        public const uint FontMarkerAddress = BaseAddress + 0x20;
        public const uint FontBaseAddress = FontMarkerAddress - 0x20;
        private const int FontHeight = 8;
        private const int FontBytesPerRow = 0x100;
        private static readonly int[] RowOffsets = { 0x000, 0x100, 0x200, 0x300, 0x400, 0x500, 0x600, 0x700 };
        private static readonly int[] CompactRowOffsets = { 0x000, 0x180, 0x240, 0x300, 0x480 };

        public static byte[] CreateTopazCompatibleFont()
        {
            var data = new byte[FontHeight * FontBytesPerRow];
            foreach (var (character, rows) in Glyphs)
            {
                WriteGlyph(data, (byte)character, rows);
                WriteGlyph(data, (byte)character, rows, CompactRowOffsets);
                if (character is >= 'A' and <= 'Z')
                {
                    WriteGlyph(data, (byte)(character + ('a' - 'A')), rows);
                    WriteGlyph(data, (byte)(character + ('a' - 'A')), rows, CompactRowOffsets);
                }
            }

            return data;
        }

        private static void WriteGlyph(byte[] data, byte index, ReadOnlySpan<byte> rows)
        {
            WriteGlyph(data, index, rows, RowOffsets);
        }

        private static void WriteGlyph(byte[] data, byte index, ReadOnlySpan<byte> rows, ReadOnlySpan<int> rowOffsets)
        {
            for (var row = 0; row < rowOffsets.Length; row++)
            {
                data[rowOffsets[row] + index] = row < rows.Length ? rows[row] : (byte)0;
            }
        }

        private static readonly IReadOnlyDictionary<char, byte[]> Glyphs = new Dictionary<char, byte[]>
        {
            [' '] = Glyph("        "),
            ['!'] = Glyph("   ## ", "   ## ", "   ## ", "      ", "   ## "),
            ['"'] = Glyph(" ## ##", " ## ##", "      ", "      ", "      "),
            ['#'] = Glyph(" ## ##", "######", " ## ##", "######", " ## ##"),
            ['\''] = Glyph("  ##  ", "  ##  ", " ##   ", "      ", "      "),
            ['('] = Glyph("   ## ", "  ##  ", "  ##  ", "  ##  ", "   ## "),
            [')'] = Glyph(" ##   ", "  ##  ", "  ##  ", "  ##  ", " ##   "),
            ['*'] = Glyph("      ", "##  ##", " #### ", "##  ##", "      "),
            ['+'] = Glyph("      ", "  ##  ", "######", "  ##  ", "      "),
            ['.'] = Glyph("      ", "      ", "      ", "      ", "  ##  "),
            [','] = Glyph("      ", "      ", "      ", "  ##  ", " ##   "),
            [':'] = Glyph("      ", "  ##  ", "      ", "  ##  ", "      "),
            ['-'] = Glyph("      ", "      ", " #### ", "      ", "      "),
            ['/'] = Glyph("    ##", "   ## ", "  ##  ", " ##   ", "##    "),
            ['0'] = Glyph(" #### ", "##  ##", "##  ##", "##  ##", " #### "),
            ['1'] = Glyph("  ##  ", " ###  ", "  ##  ", "  ##  ", " #### "),
            ['2'] = Glyph(" #### ", "##  ##", "   ## ", "  ##  ", "######"),
            ['3'] = Glyph(" #### ", "    ##", "  ### ", "    ##", " #### "),
            ['4'] = Glyph("##  ##", "##  ##", "######", "    ##", "    ##"),
            ['5'] = Glyph("######", "##    ", "##### ", "    ##", "##### "),
            ['6'] = Glyph(" #### ", "##    ", "##### ", "##  ##", " #### "),
            ['7'] = Glyph("######", "    ##", "   ## ", "  ##  ", "  ##  "),
            ['8'] = Glyph(" #### ", "##  ##", " #### ", "##  ##", " #### "),
            ['9'] = Glyph(" #### ", "##  ##", " #####", "    ##", " #### "),
            ['?'] = Glyph(" #### ", "##  ##", "   ## ", "      ", "  ##  "),
            ['A'] = Glyph(" #### ", "##  ##", "######", "##  ##", "##  ##"),
            ['B'] = Glyph("##### ", "##  ##", "##### ", "##  ##", "##### "),
            ['C'] = Glyph(" #### ", "##  ##", "##    ", "##  ##", " #### "),
            ['D'] = Glyph("##### ", "##  ##", "##  ##", "##  ##", "##### "),
            ['E'] = Glyph("######", "##    ", "##### ", "##    ", "######"),
            ['F'] = Glyph("######", "##    ", "##### ", "##    ", "##    "),
            ['G'] = Glyph(" #### ", "##    ", "## ###", "##  ##", " #### "),
            ['H'] = Glyph("##  ##", "##  ##", "######", "##  ##", "##  ##"),
            ['I'] = Glyph("######", "  ##  ", "  ##  ", "  ##  ", "######"),
            ['J'] = Glyph("  ####", "    ##", "    ##", "##  ##", " #### "),
            ['K'] = Glyph("##  ##", "## ## ", "####  ", "## ## ", "##  ##"),
            ['L'] = Glyph("##    ", "##    ", "##    ", "##    ", "######"),
            ['M'] = Glyph("##  ##", "######", "######", "##  ##", "##  ##"),
            ['N'] = Glyph("##  ##", "### ##", "######", "## ###", "##  ##"),
            ['O'] = Glyph(" #### ", "##  ##", "##  ##", "##  ##", " #### "),
            ['P'] = Glyph("##### ", "##  ##", "##### ", "##    ", "##    "),
            ['Q'] = Glyph(" #### ", "##  ##", "##  ##", "## ###", " #####"),
            ['R'] = Glyph("##### ", "##  ##", "##### ", "## ## ", "##  ##"),
            ['S'] = Glyph(" #####", "##    ", " #### ", "    ##", "##### "),
            ['T'] = Glyph("######", "  ##  ", "  ##  ", "  ##  ", "  ##  "),
            ['U'] = Glyph("##  ##", "##  ##", "##  ##", "##  ##", " #### "),
            ['V'] = Glyph("##  ##", "##  ##", "##  ##", " #### ", "  ##  "),
            ['W'] = Glyph("##  ##", "##  ##", "######", "######", "##  ##"),
            ['X'] = Glyph("##  ##", " #### ", "  ##  ", " #### ", "##  ##"),
            ['Y'] = Glyph("##  ##", " #### ", "  ##  ", "  ##  ", "  ##  "),
            ['Z'] = Glyph("######", "   ## ", "  ##  ", " ##   ", "######")
        };

        private static byte[] Glyph(params string[] rows)
        {
            var result = new byte[FontHeight];
            for (var row = 0; row < result.Length; row++)
            {
                var pattern = row < rows.Length ? rows[row] : string.Empty;
                var value = 0;
                for (var column = 0; column < Math.Min(8, pattern.Length); column++)
                {
                    if (pattern[column] != ' ')
                    {
                        value |= 0x80 >> column;
                    }
                }

                result[row] = (byte)value;
            }

            return result;
        }
    }
}
