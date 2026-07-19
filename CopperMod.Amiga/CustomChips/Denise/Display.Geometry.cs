/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;

namespace CopperMod.Amiga.CustomChips.Denise;

internal readonly record struct DisplayWindow(
    int HorizontalStart,
    int HorizontalStop,
    int VerticalStart,
    int VerticalStop);

internal readonly record struct DataFetchWindow(
    int Start,
    int Stop,
    DeniseResolution Resolution);

internal static class DisplayGeometryDecoder
{
    private const ushort Bplcon0HighResolution = 0x8000;
    private const ushort Bplcon0SuperHighResolution = 0x0040;

    public static DisplayWindow DecodeDisplayWindow(
        DmaChipModel model,
        ushort diwStart,
        ushort diwStop,
        ushort diwHigh,
        bool diwHighValid)
        => DecodeDisplayWindow(model.SupportsEcsRegisters(), diwStart, diwStop, diwHigh, diwHighValid);

    public static DisplayWindow DecodeDisplayWindow(
        DisplayChipModel model,
        ushort diwStart,
        ushort diwStop,
        ushort diwHigh,
        bool diwHighValid)
        => DecodeDisplayWindow(model.SupportsEcsRegisters(), diwStart, diwStop, diwHigh, diwHighValid);

    public static DataFetchWindow DecodeDataFetchWindow(
        DmaChipModel model,
        ushort bplcon0,
        ushort ddfStart,
        ushort ddfStop)
    {
        var resolution = GetDataFetchResolution(model, bplcon0);
        return new DataFetchWindow(
            DecodeDataFetchComparator(ddfStart, resolution),
            DecodeDataFetchComparator(ddfStop, resolution),
            resolution);
    }

    public static int GetDataFetchWordCount(
        DataFetchWindow window,
        ushort rawDdfStart,
        ushort rawDdfStop,
        int maximum)
    {
        if (window.Stop < window.Start)
        {
            return 0;
        }

        if (window.Resolution == DeniseResolution.SuperHighRes)
        {
            return Math.Clamp(((window.Stop - window.Start) / 2) + 2, 0, maximum);
        }

        if (window.Resolution == DeniseResolution.HighRes)
        {
            var words = ((window.Stop - window.Start) / 4) + 2;
            if ((words & 1) != 0)
            {
                words++;
            }

            return Math.Clamp(words, 0, maximum);
        }

        var lowResolutionWords = ((window.Stop - window.Start) / 8) + 1;
        if ((rawDdfStart & 0x0004) != 0 && (rawDdfStop & 0x0004) == 0)
        {
            // A second-half start match advances the physical first slot
            // without shortening the fetch-unit span.
            lowResolutionWords++;
        }

        return Math.Clamp(lowResolutionWords, 0, maximum);
    }

    public static int GetDataFetchSlotStride(DataFetchWindow window)
        => window.Resolution switch
        {
            DeniseResolution.SuperHighRes => 2,
            DeniseResolution.HighRes => 4,
            _ => 8
        };

    public static DeniseResolution GetDataFetchResolution(DmaChipModel model, ushort bplcon0)
    {
        if ((bplcon0 & Bplcon0HighResolution) != 0)
        {
            return DeniseResolution.HighRes;
        }

        return model.SupportsEcsRegisters() &&
            (bplcon0 & (Bplcon0HighResolution | Bplcon0SuperHighResolution)) == Bplcon0SuperHighResolution
                ? DeniseResolution.SuperHighRes
                : DeniseResolution.LowRes;
    }

    private static DisplayWindow DecodeDisplayWindow(
        bool ecs,
        ushort diwStart,
        ushort diwStop,
        ushort diwHigh,
        bool diwHighValid)
    {
        var useExtendedBits = ecs && diwHighValid;
        var horizontalStart = diwStart & 0x00FF;
        var horizontalStop = diwStop & 0x00FF;
        var verticalStart = (diwStart >> 8) & 0x00FF;
        var verticalStop = (diwStop >> 8) & 0x00FF;

        if (useExtendedBits)
        {
            horizontalStart |= (diwHigh & 0x0020) != 0 ? 0x100 : 0;
            horizontalStop |= (diwHigh & 0x2000) != 0 ? 0x100 : 0;
            if (horizontalStop <= horizontalStart)
            {
                horizontalStop += 0x200;
            }

            verticalStart |= (diwHigh & 0x000F) << 8;
            verticalStop |= diwHigh & 0x0F00;
            if (verticalStop <= verticalStart)
            {
                verticalStop += 0x1000;
            }

            return new DisplayWindow(horizontalStart, horizontalStop, verticalStart, verticalStop);
        }

        horizontalStop += 0x100;
        if (verticalStop < 0x80)
        {
            verticalStop += 0x100;
        }

        if (verticalStop <= verticalStart)
        {
            verticalStop += 0x100;
        }

        return new DisplayWindow(horizontalStart, horizontalStop, verticalStart, verticalStop);
    }

    private static int DecodeDataFetchComparator(ushort value, DeniseResolution resolution)
    {
        if (resolution == DeniseResolution.SuperHighRes)
        {
            return value & 0x00FE;
        }

        if (resolution == DeniseResolution.HighRes)
        {
            return value & 0x00FC;
        }

        var blockStart = value & 0x00F8;
        return (value & 0x0004) != 0 ? blockStart + 8 : blockStart;
    }
}
