/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections.Generic;
using System.Text;

namespace CopperMod.Amiga
{
    internal sealed partial class AmigaBootController
    {
        bool ICyberGraphicsGuestServices.TryInvokeGraphicsLibraryPatch(
            int vectorOffset,
            M68kCpuState state)
        {
            switch (vectorOffset)
            {
                case -30: // BltBitMap
                    return TryPatchBltBitMap(state);
                case -60: // Text
                    if (!CyberGraphics.IsRtgRastPort(state.A[1])) return false;
                    HostGraphicsText(state);
                    state.D[0] = 0;
                    return true;
                case -192: // LoadRGB4
                    if (!TryLoadRtgRgb4(state)) return false;
                    state.D[0] = 0;
                    return true;
                case -222: // LoadView
                    // Observe the complete View, including RTG ViewPorts below a
                    // planar front screen, but let graphics.library perform the
                    // real LoadView and Copper bookkeeping.
                    _currentViewAddress = state.A[1];
                    return false;
                case -234: // SetRast
                    if (!CyberGraphics.IsRtgRastPort(state.A[1])) return false;
                    HostGraphicsSetRast(state);
                    state.D[0] = 0;
                    return true;
                case -240: // Move
                    if (!CyberGraphics.IsRtgRastPort(state.A[1])) return false;
                    HostGraphicsMove(state);
                    state.D[0] = 0;
                    return true;
                case -246: // Draw
                    if (!CyberGraphics.IsRtgRastPort(state.A[1])) return false;
                    HostGraphicsDraw(state);
                    state.D[0] = 0;
                    return true;
                case -288: // SetRGB4
                    if (!TrySetRtgRgb4(state)) return false;
                    state.D[0] = 0;
                    return true;
                case -306: // RectFill
                    if (!CyberGraphics.IsRtgRastPort(state.A[1])) return false;
                    HostGraphicsRectFill(state);
                    state.D[0] = 0;
                    return true;
                case -552: // ClipBlit
                    return TryPatchClipBlit(state);
                case -606: // BltBitMapRastPort
                    return TryPatchBltBitMapRastPort(state);
                case -636: // BltMaskBitMapRastPort
                    return TryPatchMaskedBitMapRastPort(state);
                case -726: // FindDisplayInfo
                    if (!CyberGraphics.TryGetMode(state.D[0], out _)) return false;
                    // The RTG ModeID itself is the opaque DisplayInfoHandle.
                    return true;
                case -732: // NextDisplayInfo
                    return TryPatchNextDisplayInfo(state);
                case -756: // GetDisplayInfoData
                    return TryPatchGetDisplayInfoData(state);
                case -798: // ModeNotAvailable
                    if (!CyberGraphics.TryGetMode(state.D[0], out _)) return false;
                    state.D[0] = 0;
                    return true;
                case -918: // AllocBitMap
                    // AllocBitMap has no ModeID. A CyberGraphX friend bitmap is
                    // normally the unambiguous request for linear RTG storage.
                    // During a selected OpenScreen call, the active ModeID is an
                    // equally strong signal for Intuition's one screen bitmap.
                    CyberGraphicsPixelFormat? screenPixelFormat = null;
                    if (state.A[0] == 0 || !CyberGraphics.IsRtgBitMap(state.A[0]))
                    {
                        if (!TryGetPendingRtgScreenAllocation(state, out var screenMode)) return false;
                        screenPixelFormat = screenMode.PixelFormat;
                    }

                    state.D[0] = HostGraphicsAllocBitMap(state, screenPixelFormat);
                    if (screenPixelFormat.HasValue)
                    {
                        RecordPendingRtgScreenBitMap(state.D[0]);
                    }
                    return true;
                case -924: // FreeBitMap
                    if (!CyberGraphics.IsRtgBitMap(state.A[0])) return false;
                    HostGraphicsFreeBitMap(state.A[0]);
                    state.D[0] = 0;
                    return true;
                case -942: // ChangeVPBitMap
                    if (!CyberGraphics.IsRtgBitMap(state.A[1])) return false;
                    state.D[0] = CyberGraphics.ChangeViewPortBitMap(state.A[0], state.A[1]) ? 0u : 1u;
                    return true;
                case -960: // GetBitMapAttr
                    if (!CyberGraphics.IsRtgBitMap(state.A[0])) return false;
                    state.D[0] = HostGraphicsGetBitMapAttr(state.A[0], state.D[1]);
                    return true;
                case -1050: // BestModeIDA
                    state.D[0] = FindBestRtgDisplayMode(state.A[0]);
                    return state.D[0] != CyberGraphicsLibrary.InvalidDisplayId;
                default:
                    return false;
            }
        }

        private bool TryPatchNextDisplayInfo(M68kCpuState state)
        {
            var modes = CyberGraphics.RegisteredModes;
            if (modes.Count == 0)
            {
                return false;
            }

            if (state.D[0] == CyberGraphicsLibrary.InvalidDisplayId)
            {
                state.D[0] = modes[0].DisplayId;
                return true;
            }

            for (var index = 0; index < modes.Count; index++)
            {
                if (modes[index].DisplayId != state.D[0])
                {
                    continue;
                }

                if (index + 1 < modes.Count)
                {
                    state.D[0] = modes[index + 1].DisplayId;
                    return true;
                }

                // Continue into the native database after the last RTG mode.
                state.D[0] = CyberGraphicsLibrary.InvalidDisplayId;
                return false;
            }

            return false;
        }

        private bool TryPatchGetDisplayInfoData(M68kCpuState state)
        {
            var displayId = state.D[2];
            if (!CyberGraphics.TryGetMode(displayId, out var mode) &&
                !CyberGraphics.TryGetMode(state.A[0], out mode))
            {
                return false;
            }

            var data = BuildDisplayInfoData(mode, state.D[1]);
            if (data.Length == 0 || state.A[1] == 0)
            {
                state.D[0] = 0;
                return true;
            }

            var byteCount = (int)Math.Min(state.D[0], (uint)data.Length);
            if (byteCount <= 0 || !_machine.Bus.IsMappedMemoryRange(state.A[1], byteCount))
            {
                state.D[0] = 0;
                return true;
            }

            for (var index = 0; index < byteCount; index++)
            {
                _machine.Bus.WriteByte(state.A[1] + (uint)index, data[index], 0);
            }

            state.D[0] = (uint)byteCount;
            return true;
        }

        private byte[] BuildDisplayInfoData(CyberGraphicsMode mode, uint tag)
        {
            const uint dtagDisp = 0x8000_0000;
            const uint dtagDims = 0x8000_1000;
            const uint dtagMntr = 0x8000_2000;
            const uint dtagName = 0x8000_3000;
            byte[] data;
            switch (tag)
            {
                case dtagDisp:
                    data = new byte[0x30];
                    WriteLong(data, 0x12, 0x0050_0100); // WB, DBUFFER, FOREIGN
                    WriteWord(data, 0x16, 1);
                    WriteWord(data, 0x18, 1);
                    WriteWord(data, 0x1A, 1);
                    WriteWord(data, 0x1E, (ushort)(1 << Math.Min(mode.Depth, (ushort)8)));
                    WriteWord(data, 0x20, 1);
                    WriteWord(data, 0x22, 1);
                    break;
                case dtagDims:
                    data = new byte[0x58];
                    WriteWord(data, 0x10, mode.Depth);
                    WriteWord(data, 0x12, mode.Width);
                    WriteWord(data, 0x14, mode.Height);
                    WriteWord(data, 0x16, mode.Width);
                    WriteWord(data, 0x18, mode.Height);
                    for (var rectangle = 0; rectangle < 5; rectangle++)
                    {
                        WriteRectangle(data, 0x1A + rectangle * 8, mode.Width, mode.Height);
                    }
                    break;
                case dtagMntr:
                    data = new byte[0x60];
                    WriteWord(data, 0x18, 1);
                    WriteWord(data, 0x1A, 1);
                    WriteRectangle(data, 0x1C, mode.Width, mode.Height);
                    WriteWord(data, 0x24, mode.Height);
                    WriteWord(data, 0x26, mode.Width);
                    WriteWord(data, 0x2A, 1); // MCOMPAT_SELF
                    WriteLong(data, 0x54, mode.DisplayId);
                    break;
                case dtagName:
                    data = new byte[0x38];
                    var name = Encoding.ASCII.GetBytes(mode.Name);
                    Array.Copy(name, 0, data, 0x10, Math.Min(31, name.Length));
                    break;
                default:
                    return Array.Empty<byte>();
            }

            WriteLong(data, 0x00, tag);
            WriteLong(data, 0x04, mode.DisplayId);
            WriteLong(data, 0x08, CyberGraphicsLibrary.TagSkip);
            WriteLong(data, 0x0C, (uint)((data.Length - 16) / 8));
            return data;
        }

        private uint FindBestRtgDisplayMode(uint tagList)
        {
            const uint bidMustHave = 0x8000_0001;
            const uint bidMustNotHave = 0x8000_0002;
            const uint bidNominalWidth = 0x8000_0004;
            const uint bidNominalHeight = 0x8000_0005;
            const uint bidDesiredWidth = 0x8000_0006;
            const uint bidDesiredHeight = 0x8000_0007;
            const uint bidDepth = 0x8000_0008;
            const uint bidMonitorId = 0x8000_0009;
            const uint rtgProperties = 0x0050_0100;
            var tags = ReadPatchTags(tagList);
            var nominalWidth = GetTag(tags, bidNominalWidth, 640);
            var nominalHeight = GetTag(tags, bidNominalHeight, 200);
            var desiredWidth = GetTag(tags, bidDesiredWidth, nominalWidth);
            var desiredHeight = GetTag(tags, bidDesiredHeight, nominalHeight);
            var minimumDepth = GetTag(tags, bidDepth, 1);
            var monitorId = GetTag(tags, bidMonitorId, 0);
            var mustHave = GetTag(tags, bidMustHave, 0);
            var mustNotHave = GetTag(tags, bidMustNotHave, 0x0000_100E);
            CyberGraphicsMode? best = null;
            long bestScore = long.MaxValue;
            foreach (var mode in CyberGraphics.RegisteredModes)
            {
                if (mode.Depth < minimumDepth ||
                    (monitorId != 0 && mode.MonitorId != monitorId) ||
                    (rtgProperties & mustHave) != mustHave ||
                    (rtgProperties & mustNotHave) != 0)
                {
                    continue;
                }

                var aspectError = Math.Abs((long)mode.Width * nominalHeight - (long)mode.Height * nominalWidth);
                var sizeError = Math.Abs((long)mode.Width - desiredWidth) + Math.Abs((long)mode.Height - desiredHeight);
                var depthError = mode.Depth - minimumDepth;
                var score = aspectError * 1024 + sizeError * 16 + depthError;
                if (score < bestScore)
                {
                    best = mode;
                    bestScore = score;
                }
            }

            return best?.DisplayId ?? CyberGraphicsLibrary.InvalidDisplayId;
        }

        private Dictionary<uint, uint> ReadPatchTags(uint address)
        {
            var result = new Dictionary<uint, uint>();
            var visited = new HashSet<uint>();
            for (var count = 0; address != 0 && count < 512; count++)
            {
                if (!visited.Add(address) || !_machine.Bus.IsMappedMemoryRange(address, 8)) break;
                var tag = _machine.Bus.ReadLong(address);
                var value = _machine.Bus.ReadLong(address + 4);
                address += 8;
                switch (tag)
                {
                    case CyberGraphicsLibrary.TagDone: return result;
                    case CyberGraphicsLibrary.TagIgnore: continue;
                    case CyberGraphicsLibrary.TagMore: address = value; continue;
                    case CyberGraphicsLibrary.TagSkip: address += value * 8; continue;
                    default: result[tag] = value; break;
                }
            }

            return result;
        }

        private static uint GetTag(Dictionary<uint, uint>? tags, uint tag, uint fallback)
            => tags != null && tags.TryGetValue(tag, out var value) ? value : fallback;

        private static void WriteRectangle(byte[] data, int offset, ushort width, ushort height)
        {
            WriteWord(data, offset + 0, 0);
            WriteWord(data, offset + 2, 0);
            WriteWord(data, offset + 4, (ushort)(width - 1));
            WriteWord(data, offset + 6, (ushort)(height - 1));
        }

        private static void WriteWord(byte[] data, int offset, ushort value)
        {
            data[offset] = (byte)(value >> 8);
            data[offset + 1] = (byte)value;
        }

        private static void WriteLong(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)(value >> 24);
            data[offset + 1] = (byte)(value >> 16);
            data[offset + 2] = (byte)(value >> 8);
            data[offset + 3] = (byte)value;
        }

        private bool TryPatchBltBitMap(M68kCpuState state)
        {
            var sourceIsRtg = CyberGraphics.IsRtgBitMap(state.A[0]);
            var destinationIsRtg = CyberGraphics.IsRtgBitMap(state.A[1]);
            if (!sourceIsRtg && !destinationIsRtg)
            {
                return false;
            }

            if (!destinationIsRtg)
            {
                // RTG-to-planar needs a format-aware chunky-to-planar path. Do
                // not let Kickstart interpret linear VRAM as bitplane pointers.
                state.D[0] = 0;
                return true;
            }

            var pixels = sourceIsRtg
                ? CyberGraphics.BlitRtgToRtg(
                    state.A[0], S(state.D[0]), S(state.D[1]),
                    state.A[1], S(state.D[2]), S(state.D[3]),
                    S(state.D[4]), S(state.D[5]), (byte)state.D[6], (byte)state.D[7])
                : CyberGraphics.BlitPlanarToRtg(
                    state.A[0], S(state.D[0]), S(state.D[1]),
                    state.A[1], S(state.D[2]), S(state.D[3]),
                    S(state.D[4]), S(state.D[5]), (byte)state.D[6], (byte)state.D[7]);
            state.D[0] = pixels == 0 ? 0u : 1u;
            return true;
        }

        private bool TryPatchClipBlit(M68kCpuState state)
        {
            if (!TryGetRastPortBitMap(state.A[0], out var source) ||
                !TryGetRastPortBitMap(state.A[1], out var destination) ||
                (!CyberGraphics.IsRtgBitMap(source) && !CyberGraphics.IsRtgBitMap(destination)))
            {
                return false;
            }

            return TryPatchBitMapToRastPort(state, source, destination, ReadRastPortMask(state.A[1]));
        }

        private bool TryPatchBltBitMapRastPort(M68kCpuState state)
        {
            if (!TryGetRastPortBitMap(state.A[1], out var destination) ||
                (!CyberGraphics.IsRtgBitMap(state.A[0]) && !CyberGraphics.IsRtgBitMap(destination)))
            {
                return false;
            }

            return TryPatchBitMapToRastPort(state, state.A[0], destination, ReadRastPortMask(state.A[1]));
        }

        private bool TryPatchMaskedBitMapRastPort(M68kCpuState state)
        {
            if (!TryGetRastPortBitMap(state.A[1], out var destination) ||
                !CyberGraphics.IsRtgBitMap(destination))
            {
                return false;
            }

            if (CyberGraphics.IsRtgBitMap(state.A[0]))
            {
                state.D[0] = 0;
                return true;
            }

            var pixels = CyberGraphics.BlitPlanarToRtg(
                state.A[0], S(state.D[0]), S(state.D[1]),
                destination, S(state.D[2]), S(state.D[3]),
                S(state.D[4]), S(state.D[5]), (byte)state.D[6],
                ReadRastPortMask(state.A[1]), state.A[2]);
            state.D[0] = pixels == 0 ? 0u : 1u;
            return true;
        }

        private bool TryPatchBitMapToRastPort(
            M68kCpuState state,
            uint source,
            uint destination,
            byte writeMask = 0xFF)
        {
            var sourceIsRtg = CyberGraphics.IsRtgBitMap(source);
            if (!CyberGraphics.IsRtgBitMap(destination))
            {
                state.D[0] = 0;
                return true;
            }

            var pixels = sourceIsRtg
                ? CyberGraphics.BlitRtgToRtg(
                    source, S(state.D[0]), S(state.D[1]),
                    destination, S(state.D[2]), S(state.D[3]),
                    S(state.D[4]), S(state.D[5]), (byte)state.D[6], writeMask)
                : CyberGraphics.BlitPlanarToRtg(
                    source, S(state.D[0]), S(state.D[1]),
                    destination, S(state.D[2]), S(state.D[3]),
                    S(state.D[4]), S(state.D[5]), (byte)state.D[6], writeMask);
            state.D[0] = pixels == 0 ? 0u : 1u;
            return true;
        }

        private bool TryLoadRtgRgb4(M68kCpuState state)
        {
            if (!CyberGraphics.TryGetViewPortSurface(state.A[0], out var surface))
            {
                return false;
            }

            var count = (int)Math.Min(state.D[0], 256u);
            if (count <= 0 || state.A[1] == 0 || !_machine.Bus.IsMappedMemoryRange(state.A[1], count * 2))
            {
                return true;
            }

            for (var index = 0; index < count; index++)
            {
                var rgb = _machine.Bus.ReadWord(state.A[1] + (uint)(index * 2));
                var red = (uint)((rgb >> 8) & 0x0F) * 17;
                var green = (uint)((rgb >> 4) & 0x0F) * 17;
                var blue = (uint)(rgb & 0x0F) * 17;
                surface.Palette[index] = 0xFF00_0000u | (red << 16) | (green << 8) | blue;
            }

            return true;
        }

        private bool TrySetRtgRgb4(M68kCpuState state)
        {
            if (!CyberGraphics.TryGetViewPortSurface(state.A[0], out var surface))
            {
                return false;
            }

            var index = (int)state.D[0];
            if ((uint)index < surface.Palette.Length)
            {
                var red = (state.D[1] & 0x0F) * 17;
                var green = (state.D[2] & 0x0F) * 17;
                var blue = (state.D[3] & 0x0F) * 17;
                surface.Palette[index] = 0xFF00_0000u | (red << 16) | (green << 8) | blue;
            }

            return true;
        }

        private byte ReadRastPortMask(uint rastPort)
            => rastPort != 0 && _machine.Bus.IsMappedMemoryRange(rastPort + RastPortMaskOffset, 1)
                ? _machine.Bus.ReadByte(rastPort + RastPortMaskOffset)
                : (byte)0xFF;

        private static int S(uint value)
            => unchecked((short)(ushort)value);
    }
}
