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
        Rsid
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
            IReadOnlyList<ModuleDiagnostic> diagnostics)
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

        public bool IsRsid => Kind == SidFileKind.Rsid;

        public bool IsBasicRsid => IsRsid && ((Flags >> 1) & 1) != 0;

        public SidChipModel EffectiveChipModel => ChipModel == SidChipModel.Mos8580 ? SidChipModel.Mos8580 : SidChipModel.Mos6581;
    }
}
