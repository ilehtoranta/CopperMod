using System;
using System.Collections.Generic;
using CopperMod.Abstractions;

namespace CopperMod.Sid
{
    /// <summary>
    /// SID container family.
    /// </summary>
    internal enum SidFileKind
    {
        Psid,
        Rsid,
        Crt
    }

    /// <summary>
    /// C64 video standard requested by a SID file.
    /// </summary>
    internal enum SidClock
    {
        Unknown,
        Pal,
        Ntsc,
        PalAndNtsc
    }

    /// <summary>
    /// SID chip model requested by a SID file.
    /// </summary>
    internal enum SidChipModel
    {
        Unknown,
        Mos6581,
        Mos8580,
        Any
    }

    /// <summary>
    /// One mapped SID chip.
    /// </summary>
    internal sealed class SidChipPlacement
    {
        public SidChipPlacement(int index, ushort baseAddress)
        {
            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            Index = index;
            BaseAddress = baseAddress;
        }

        public int Index { get; }

        public ushort BaseAddress { get; }
    }

    /// <summary>
    /// Parsed PSID/RSID module data.
    /// </summary>
    internal sealed class SidModule
    {
        public SidModule(
            SidFileKind kind,
            int version,
            int dataOffset,
            ushort loadAddress,
            ushort effectiveLoadAddress,
            ushort initAddress,
            ushort playAddress,
            int subSongCount,
            int defaultSubSongIndex,
            uint speed,
            ushort flags,
            byte relocationStartPage,
            byte relocationPageLength,
            string title,
            string author,
            string released,
            SidClock clock,
            SidChipModel chipModel,
            IReadOnlyList<SidChipPlacement> chips,
            byte[] payload,
            IReadOnlyList<ModuleDiagnostic> diagnostics,
            C64CartridgeImage? cartridge = null,
            bool preferRomBasic = false)
        {
            Kind = kind;
            Version = version;
            DataOffset = dataOffset;
            LoadAddress = loadAddress;
            EffectiveLoadAddress = effectiveLoadAddress;
            InitAddress = initAddress;
            PlayAddress = playAddress;
            SubSongCount = subSongCount;
            DefaultSubSongIndex = defaultSubSongIndex;
            Speed = speed;
            Flags = flags;
            RelocationStartPage = relocationStartPage;
            RelocationPageLength = relocationPageLength;
            Title = title;
            Author = author;
            Released = released;
            Clock = clock;
            ChipModel = chipModel;
            Chips = chips;
            Payload = payload;
            Diagnostics = diagnostics;
            Cartridge = cartridge;
            PreferRomBasic = preferRomBasic;
        }

        public SidFileKind Kind { get; }

        public int Version { get; }

        public int DataOffset { get; }

        public ushort LoadAddress { get; }

        public ushort EffectiveLoadAddress { get; }

        public ushort InitAddress { get; }

        public ushort PlayAddress { get; }

        public int SubSongCount { get; }

        public int DefaultSubSongIndex { get; }

        public uint Speed { get; }

        public ushort Flags { get; }

        public byte RelocationStartPage { get; }

        public byte RelocationPageLength { get; }

        public string Title { get; }

        public string Author { get; }

        public string Released { get; }

        public SidClock Clock { get; }

        public SidChipModel ChipModel { get; }

        public IReadOnlyList<SidChipPlacement> Chips { get; }

        public byte[] Payload { get; }

        public IReadOnlyList<ModuleDiagnostic> Diagnostics { get; }

        public C64CartridgeImage? Cartridge { get; }

        public bool PreferRomBasic { get; }

        public bool IsRsid => Kind == SidFileKind.Rsid;

        public bool IsCartridge => Kind == SidFileKind.Crt;

        public bool IsBasicRsid => IsRsid && ((Flags >> 1) & 1) != 0;

        public bool RunsContinuously => IsRsid || IsCartridge || IsBasicRsid || (Kind == SidFileKind.Psid && PlayAddress == 0);

        public SidChipModel EffectiveChipModel => ChipModel == SidChipModel.Mos8580 ? SidChipModel.Mos8580 : SidChipModel.Mos6581;

        public static SidModule CreateEasyFlashCartridge(C64CartridgeImage cartridge)
        {
            ArgumentNullException.ThrowIfNull(cartridge);
            return new SidModule(
                SidFileKind.Crt,
                version: 0,
                dataOffset: 0,
                loadAddress: 0,
                effectiveLoadAddress: 0,
                initAddress: 0,
                playAddress: 0,
                subSongCount: 1,
                defaultSubSongIndex: 0,
                speed: 0,
                flags: 0,
                relocationStartPage: 0,
                relocationPageLength: 0,
                title: string.IsNullOrWhiteSpace(cartridge.Name) ? "EasyFlash Cartridge" : cartridge.Name,
                author: "C64 Cartridge",
                released: "",
                clock: SidClock.Pal,
                chipModel: SidChipModel.Mos6581,
                chips: new[] { new SidChipPlacement(0, SidConstants.DefaultSidBaseAddress) },
                payload: Array.Empty<byte>(),
                diagnostics: Array.Empty<ModuleDiagnostic>(),
                cartridge: cartridge);
        }

        public static SidModule CreateBasicProgram(ReadOnlySpan<byte> data, string title)
        {
            if (data.Length < 3)
            {
                throw new ModuleLoadException("C64 PRG data is shorter than the load address and payload.");
            }

            var loadAddress = (ushort)(data[0] | (data[1] << 8));
            var payload = data.Slice(2).ToArray();
            if (loadAddress + payload.Length > 0x10000)
            {
                throw new ModuleLoadException("C64 PRG payload does not fit in memory.");
            }

            return new SidModule(
                SidFileKind.Rsid,
                version: 2,
                dataOffset: 2,
                loadAddress: 0,
                effectiveLoadAddress: loadAddress,
                initAddress: 0,
                playAddress: 0,
                subSongCount: 1,
                defaultSubSongIndex: 0,
                speed: 0,
                flags: 0x0002,
                relocationStartPage: 0,
                relocationPageLength: 0,
                title: string.IsNullOrWhiteSpace(title) ? "C64 PRG" : title,
                author: "C64 PRG",
                released: "",
                clock: SidClock.Pal,
                chipModel: SidChipModel.Mos6581,
                chips: new[] { new SidChipPlacement(0, SidConstants.DefaultSidBaseAddress) },
                payload: payload,
                diagnostics: new[]
                {
                    new ModuleDiagnostic(
                        ModuleDiagnosticSeverity.Info,
                        "C64 PRG BASIC program can be executed by the ROM BASIC path when a C64 ROM is selected.",
                        "SID_PRG_BASIC_NATIVE_RUNNER")
                },
                preferRomBasic: true);
        }
    }
}
