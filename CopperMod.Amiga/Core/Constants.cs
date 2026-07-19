/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;

namespace CopperMod.Amiga.Core
{
    internal readonly record struct RasterTiming(
        int CpuCyclesPerColorClock,
        int ShortLineColorClocks,
        int LongLineColorClocks,
        bool AlternatingLineLengths,
        int ShortFrameLines,
        int LongFrameLines,
        long MasterClockHz,
        int StandardLowResWidth,
        int StandardLowResHeight,
        int PresentationLowResWidth,
        int PresentationLowResHeight)
    {
        public static RasterTiming Pal { get; } = new(
            AmigaConstants.A500PalCpuCyclesPerColorClock,
            AmigaConstants.A500PalColorClocksPerRasterLine,
            AmigaConstants.A500PalColorClocksPerRasterLine,
            false,
            AmigaConstants.A500PalShortRasterLines,
            AmigaConstants.A500PalLongRasterLines,
            28_375_160,
            AmigaConstants.PalLowResStandardWidth,
            AmigaConstants.PalLowResStandardHeight,
            AmigaConstants.PalLowResWidth,
            AmigaConstants.PalLowResHeight);

        public static RasterTiming Ntsc { get; } = new(
            AmigaConstants.A500NtscCpuCyclesPerColorClock,
            AmigaConstants.A500NtscShortLineColorClocks,
            AmigaConstants.A500NtscLongLineColorClocks,
            true,
            AmigaConstants.A500NtscShortRasterLines,
            AmigaConstants.A500NtscLongRasterLines,
            28_636_360,
            AmigaConstants.NtscLowResStandardWidth,
            AmigaConstants.NtscLowResStandardHeight,
            AmigaConstants.NtscLowResWidth,
            AmigaConstants.NtscLowResHeight);

        public static RasterTiming For(VideoStandard standard)
            => standard == VideoStandard.Ntsc ? Ntsc : Pal;

        public int ColorClocksPerLine => ShortLineColorClocks;

        public int MaximumColorClocksPerLine => Math.Max(ShortLineColorClocks, LongLineColorClocks);

        public int CpuCyclesPerLine => CpuCyclesPerColorClock * ColorClocksPerLine;

        public int MaximumCpuCyclesPerLine => CpuCyclesPerColorClock * MaximumColorClocksPerLine;

        public long CpuClockHz => MasterClockHz / 4;

        public long PaulaClockHz => MasterClockHz / 8;

        public long CpuCyclesPerCiaTick => CpuCyclesPerColorClock * 5L;

        public int PresentationHighResWidth => PresentationLowResWidth * 2;

        public int PresentationSuperHighResWidth => PresentationLowResWidth * 4;

        public int PresentationHighResHeight => PresentationLowResHeight * 2;

        public double VBlankHz => CpuClockHz / (double)GetFrameCycles(LongFrameLines);

        public bool IsCanonicalPal =>
            CpuCyclesPerColorClock == AmigaConstants.A500PalCpuCyclesPerColorClock &&
            ShortLineColorClocks == AmigaConstants.A500PalColorClocksPerRasterLine &&
            LongLineColorClocks == AmigaConstants.A500PalColorClocksPerRasterLine &&
            LongFrameLines == AmigaConstants.A500PalLongRasterLines &&
            !AlternatingLineLengths;

        public bool IsCanonicalNtsc =>
            CpuCyclesPerColorClock == AmigaConstants.A500NtscCpuCyclesPerColorClock &&
            ShortLineColorClocks == AmigaConstants.A500NtscShortLineColorClocks &&
            LongLineColorClocks == AmigaConstants.A500NtscLongLineColorClocks &&
            LongFrameLines == AmigaConstants.A500NtscLongRasterLines &&
            AlternatingLineLengths;

        public int GetColorClocksForLine(int line)
            => AlternatingLineLengths && (line & 1) != 0
                ? LongLineColorClocks
                : ShortLineColorClocks;

        public long GetFrameCycles(int rasterLines)
        {
            if (!AlternatingLineLengths)
            {
                return (long)rasterLines * CpuCyclesPerLine;
            }

            var shortLines = (rasterLines + 1) / 2;
            var longLines = rasterLines / 2;
            return ((long)shortLines * ShortLineColorClocks +
                ((long)longLines * LongLineColorClocks)) * CpuCyclesPerColorClock;
        }
    }

    internal static class AmigaConstants
    {
        public const int A500PalPaulaTicksPerSecond = 3_546_895;
        public const int A500PalCpuCyclesPerColorClock = 2;
        public const int A500PalCpuCyclesPerCiaTick = A500PalCpuCyclesPerColorClock * 5;
        public const int A500PalCpuCyclesPerSecond = A500PalPaulaTicksPerSecond * A500PalCpuCyclesPerColorClock;
        public const double A500PalPaulaClockHz = A500PalPaulaTicksPerSecond;
        public const double A500PalCpuClockHz = A500PalCpuCyclesPerSecond;
        public const double A500PalCiaClockHz = A500PalCpuClockHz / A500PalCpuCyclesPerCiaTick;
        public const int A500PalColorClocksPerRasterLine = 227;
        public const int A500PalShortRasterLines = 312;
        public const int A500PalLongRasterLines = 313;
        public const int A500PalRasterLines = A500PalLongRasterLines;
        public const int A500PalCpuCyclesPerRasterLine = A500PalCpuCyclesPerColorClock * A500PalColorClocksPerRasterLine;
        public const int A500PalCpuCyclesPerFrame = A500PalCpuCyclesPerRasterLine * A500PalRasterLines;
        public const double A500PalVBlankHz = A500PalCpuClockHz / A500PalCpuCyclesPerFrame;
        public const int A500IntreqToIplDelayDmaCycles = 4;
        public const int A500IntreqToIplDelayCpuCycles = A500IntreqToIplDelayDmaCycles * A500PalCpuCyclesPerColorClock;
        public const int A500CopperIntreqDelayColorClocks = 2;
        public const int A500CopperIntreqDelayCpuCycles = A500CopperIntreqDelayColorClocks * A500PalCpuCyclesPerColorClock;
        public const ushort IntreqDiskBlock = 0x0002;
        public const ushort IntreqPorts = 0x0008;
        public const ushort IntreqCopper = 0x0010;
        public const ushort IntreqVerticalBlank = 0x0020;
        public const ushort IntreqBlitter = 0x0040;
        public const ushort IntreqExternal = 0x2000;
        public const int PaulaChannelCount = 4;
        public const int A500PalMinimumAudioDmaPeriod = 124;
        public const int A500NtscPaulaTicksPerSecond = 3_579_545;
        public const int A500NtscCpuCyclesPerColorClock = 2;
        public const int A500NtscShortLineColorClocks = 227;
        public const int A500NtscLongLineColorClocks = 228;
        public const int A500NtscShortRasterLines = 262;
        public const int A500NtscLongRasterLines = 263;
        public const int DefaultChipRamSize = 1 * 1024 * 1024;
        public const int MaxChipRamSize = 2 * 1024 * 1024;
        public const uint ChipRamCpuDecodeSize = 0x0020_0000;
        public const uint OcsChipDmaAddressMask = 0x000F_FFFEu;
        public const uint EcsChipDmaAddressMask = 0x001F_FFFEu;
        public const int A500BootChipRamSize = 512 * 1024;
        public const int A500BootPseudoFastRamSize = 512 * 1024;
        public const uint A500BootPseudoFastRamBase = 0x00C0_0000;
        public const int A500JitRealFastRamSize = 2 * 1024 * 1024;
        public const uint A500RealFastRamBase = 0x0020_0000;
        public const int PalLowResStandardWidth = 320;
        public const int PalLowResStandardHeight = 256;
        // Deep PAL overscan is asymmetric horizontally: the standard display
        // starts 32 low-res pixels into a 358-pixel capture field.
        public const int PalLowResOverscanBorderX = 32;
        public const int PalLowResOverscanBorderY = 18;
        public const int PalLowResWidth = 358;
        public const int PalLowResHeight = 285;
        public const int PalHighResWidth = 716;
        public const int PalSuperHighResWidth = 1432;
        public const int PalHighResHeight = 570;
        public const int NtscLowResStandardWidth = 320;
        public const int NtscLowResStandardHeight = 200;
        public const int NtscLowResWidth = 362;
        public const int NtscLowResHeight = 241;
    }
}
