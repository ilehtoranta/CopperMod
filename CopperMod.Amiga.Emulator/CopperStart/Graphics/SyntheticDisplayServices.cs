/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CopperStart.Intuition;

namespace CopperMod.Amiga.CopperStart.Graphics;

/// <summary>
/// CopperStart-only planar backing-store renderer.  It deliberately uses guest
/// RAM only; copper publication and custom-chip writes remain with the normal
/// emulator bus/scheduler path.
/// </summary>
internal sealed class SyntheticDisplayServices
{
    private readonly HostGuestMemory _memory;
    private readonly SyntheticUiDisplayState _state;
    private readonly Func<char, ulong> _glyph;

    public SyntheticDisplayServices(HostGuestMemory memory, SyntheticUiDisplayState state, Func<char, ulong> glyph)
    {
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _glyph = glyph ?? throw new ArgumentNullException(nameof(glyph));
    }

    public int BytesPerRow => Math.Max(2, ((_state.ScreenWidth + 15) / 16) * 2);
    public int PlaneSize => BytesPerRow * _state.ScreenHeight;

    public void WriteBitMap(uint bitMapAddress, int bytesPerRowOffset, int rowsOffset, int depthOffset, int planesOffset)
    {
        if (bitMapAddress == 0 || _state.PlaneAddress == 0) return;
        Clear(bitMapAddress, planesOffset + 6 * 4);
        _memory.WriteWord(bitMapAddress + (uint)bytesPerRowOffset, (ushort)BytesPerRow);
        _memory.WriteWord(bitMapAddress + (uint)rowsOffset, (ushort)_state.ScreenHeight);
        _memory.WriteByte(bitMapAddress + (uint)depthOffset, (byte)_state.ScreenDepth);
        for (var plane = 0; plane < _state.ScreenDepth; plane++)
        {
            _memory.WriteLong(bitMapAddress + (uint)planesOffset + (uint)(plane * 4), _state.PlaneAddress + (uint)(plane * PlaneSize));
        }
    }

    public void RenderTitle(string title, int titleHeight)
    {
        ClearBackingStore();
        FillRect(0, 0, _state.ScreenWidth, titleHeight, 1);
        DrawText(title, 8, 6, 2);
    }

    public void ClearBackingStore()
    {
        if (_state.PlaneAddress != 0) Clear(_state.PlaneAddress, PlaneSize * _state.ScreenDepth);
    }

    public void FillRect(int x, int y, int width, int height, int color)
    {
        var x0 = Math.Clamp(x, 0, _state.ScreenWidth);
        var y0 = Math.Clamp(y, 0, _state.ScreenHeight);
        var x1 = Math.Clamp(x + Math.Max(0, width), 0, _state.ScreenWidth);
        var y1 = Math.Clamp(y + Math.Max(0, height), 0, _state.ScreenHeight);
        for (var py = y0; py < y1; py++) for (var px = x0; px < x1; px++) WritePixel(px, py, color);
    }

    public void DrawText(string text, int x, int y, int color)
    {
        var cursorX = x;
        var scale = text.Length <= 28 ? 2 : 1;
        var characterWidth = (5 * scale) + scale;
        var count = Math.Min(text.Length, Math.Max(1, (_state.ScreenWidth - x - 8) / characterWidth));
        for (var index = 0; index < count; index++)
        {
            var glyph = _glyph(text[index]);
            for (var row = 0; row < 7; row++) for (var column = 0; column < 5; column++)
            {
                if ((glyph & ((ulong)(0x10 >> column) << ((6 - row) * 5))) == 0) continue;
                for (var sy = 0; sy < scale; sy++) for (var sx = 0; sx < scale; sx++) WritePixel(cursorX + column * scale + sx, y + row * scale + sy, color);
            }
            cursorX += characterWidth;
        }
    }

    public void WritePixel(int x, int y, int color)
    {
        if (_state.PlaneAddress == 0 || x < 0 || x >= _state.ScreenWidth || y < 0 || y >= _state.ScreenHeight) return;
        var byteOffset = (y * BytesPerRow) + (x >> 3);
        var mask = (byte)(0x80 >> (x & 7));
        for (var plane = 0; plane < _state.ScreenDepth; plane++)
        {
            var address = _state.PlaneAddress + (uint)(plane * PlaneSize + byteOffset);
            var value = _memory.ReadByte(address);
            value = ((color >> plane) & 1) != 0 ? (byte)(value | mask) : (byte)(value & (byte)~mask);
            _memory.WriteByte(address, value);
        }
    }

    private void Clear(uint address, int length)
    {
        if (!_memory.IsMapped(address, length)) return;
        for (var offset = 0; offset < length; offset++) _memory.WriteByte(address + (uint)offset, 0);
    }
}
