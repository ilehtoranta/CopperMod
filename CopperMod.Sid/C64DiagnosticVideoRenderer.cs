using System;
using System.Collections.Generic;

namespace CopperMod.Sid
{
    internal static class C64DiagnosticVideoRenderer
    {
        public const int Width = 384;
        public const int Height = 272;
        private const int DisplayWidth = 320;
        private const int DisplayHeight = 200;
        private const int DisplayX = (Width - DisplayWidth) / 2;
        private const int DisplayY = (Height - DisplayHeight) / 2;
        private const int Columns = 40;
        private const int Rows = 25;
        private static readonly Argb32[] Palette =
        {
            new(255, 0x00, 0x00, 0x00),
            new(255, 0xFF, 0xFF, 0xFF),
            new(255, 0x88, 0x40, 0x00),
            new(255, 0xAA, 0xFF, 0xEE),
            new(255, 0xCC, 0x44, 0xCC),
            new(255, 0x00, 0xCC, 0x55),
            new(255, 0x00, 0x00, 0xAA),
            new(255, 0xEE, 0xEE, 0x77),
            new(255, 0xDD, 0x88, 0x55),
            new(255, 0x66, 0x44, 0x00),
            new(255, 0xFF, 0x77, 0x77),
            new(255, 0x33, 0x33, 0x33),
            new(255, 0x77, 0x77, 0x77),
            new(255, 0xAA, 0xFF, 0x66),
            new(255, 0x00, 0x88, 0xFF),
            new(255, 0xBB, 0xBB, 0xBB)
        };

        public static C64VideoFrame Render(
            byte[] ram,
            byte[] colorRam,
            byte[] vicRegisters,
            Func<ushort, byte> readVicMemory,
            int vicBankBase,
            IReadOnlyList<C64SpriteRegisterSnapshot>? spriteRegisterSnapshots,
            long frameNumber,
            TimeSpan sourceTime)
        {
            ArgumentNullException.ThrowIfNull(ram);
            ArgumentNullException.ThrowIfNull(colorRam);
            ArgumentNullException.ThrowIfNull(vicRegisters);
            ArgumentNullException.ThrowIfNull(readVicMemory);

            var pixels = new Argb32[Width * Height];
            Fill(pixels, GetColor(vicRegisters[0x20]));

            var bitmapMode = (vicRegisters[0x11] & 0x20) != 0;
            var extendedColorMode = !bitmapMode && (vicRegisters[0x11] & 0x40) != 0;
            var multicolorMode = (vicRegisters[0x16] & 0x10) != 0;

            if (bitmapMode)
            {
                RenderBitmap(pixels, colorRam, vicRegisters, readVicMemory, vicBankBase, multicolorMode);
            }
            else
            {
                RenderText(pixels, colorRam, vicRegisters, readVicMemory, vicBankBase, multicolorMode, extendedColorMode);
            }

            if (spriteRegisterSnapshots != null)
            {
                foreach (var snapshot in spriteRegisterSnapshots)
                {
                    RenderSprites(pixels, snapshot.Registers, readVicMemory, snapshot.VicBankBase, snapshot.Pointers);
                }
            }

            RenderSprites(pixels, vicRegisters, readVicMemory, vicBankBase, spritePointers: null);

            return new C64VideoFrame(Width, Height, pixels, frameNumber, sourceTime);
        }

        private static void RenderText(
            Argb32[] pixels,
            byte[] colorRam,
            byte[] vicRegisters,
            Func<ushort, byte> readVicMemory,
            int vicBankBase,
            bool multicolorMode,
            bool extendedColorMode)
        {
            var screenBase = vicBankBase + ((vicRegisters[0x18] & 0xF0) << 6);
            var charBase = vicBankBase + ((vicRegisters[0x18] & 0x0E) << 10);
            var background0 = GetColor(vicRegisters[0x21]);
            var background1 = GetColor(vicRegisters[0x22]);
            var background2 = GetColor(vicRegisters[0x23]);
            var background3 = GetColor(vicRegisters[0x24]);

            for (var row = 0; row < Rows; row++)
            {
                for (var column = 0; column < Columns; column++)
                {
                    var cell = (row * Columns) + column;
                    var screenCode = readVicMemory((ushort)(screenBase + cell));
                    var color = colorRam[cell & 0x03FF] & 0x0F;
                    var charCode = extendedColorMode ? screenCode & 0x3F : screenCode;
                    var bg = extendedColorMode
                        ? (screenCode >> 6) switch
                        {
                            1 => background1,
                            2 => background2,
                            3 => background3,
                            _ => background0
                        }
                        : background0;

                    for (var y = 0; y < 8; y++)
                    {
                        var bits = readVicMemory((ushort)(charBase + (charCode * 8) + y));
                        if (multicolorMode && (color & 0x08) != 0 && !extendedColorMode)
                        {
                            DrawTextMulticolorByte(pixels, column, row, y, bits, background0, background1, background2, GetColor(color & 0x07));
                        }
                        else
                        {
                            DrawHiresByte(pixels, column, row, y, bits, bg, GetColor(color));
                        }
                    }
                }
            }
        }

        private static void RenderBitmap(
            Argb32[] pixels,
            byte[] colorRam,
            byte[] vicRegisters,
            Func<ushort, byte> readVicMemory,
            int vicBankBase,
            bool multicolorMode)
        {
            var screenBase = vicBankBase + ((vicRegisters[0x18] & 0xF0) << 6);
            var bitmapBase = vicBankBase + ((vicRegisters[0x18] & 0x08) << 10);
            var background = GetColor(vicRegisters[0x21]);

            for (var row = 0; row < Rows; row++)
            {
                for (var column = 0; column < Columns; column++)
                {
                    var cell = (row * Columns) + column;
                    var screen = readVicMemory((ushort)(screenBase + cell));
                    var colorRamValue = colorRam[cell & 0x03FF] & 0x0F;
                    var bitmapAddress = bitmapBase + (row * 320) + (column * 8);
                    for (var y = 0; y < 8; y++)
                    {
                        var bits = readVicMemory((ushort)(bitmapAddress + y));
                        if (multicolorMode)
                        {
                            DrawBitmapMulticolorByte(
                                pixels,
                                column,
                                row,
                                y,
                                bits,
                                background,
                                GetColor(screen >> 4),
                                GetColor(screen & 0x0F),
                                GetColor(colorRamValue));
                        }
                        else
                        {
                            DrawHiresByte(pixels, column, row, y, bits, GetColor(screen & 0x0F), GetColor(screen >> 4));
                        }
                    }
                }
            }
        }

        private static void DrawHiresByte(Argb32[] pixels, int column, int row, int y, byte bits, Argb32 background, Argb32 foreground)
        {
            var pixelY = DisplayY + (row * 8) + y;
            var pixelX = DisplayX + (column * 8);
            for (var x = 0; x < 8; x++)
            {
                pixels[(pixelY * Width) + pixelX + x] = (bits & (0x80 >> x)) != 0 ? foreground : background;
            }
        }

        private static void DrawTextMulticolorByte(Argb32[] pixels, int column, int row, int y, byte bits, Argb32 background0, Argb32 background1, Argb32 background2, Argb32 foreground)
        {
            DrawMulticolorByte(pixels, column, row, y, bits, background0, background1, background2, foreground);
        }

        private static void DrawBitmapMulticolorByte(Argb32[] pixels, int column, int row, int y, byte bits, Argb32 background0, Argb32 color1, Argb32 color2, Argb32 color3)
        {
            DrawMulticolorByte(pixels, column, row, y, bits, background0, color1, color2, color3);
        }

        private static void DrawMulticolorByte(Argb32[] pixels, int column, int row, int y, byte bits, Argb32 color0, Argb32 color1, Argb32 color2, Argb32 color3)
        {
            var pixelY = DisplayY + (row * 8) + y;
            var pixelX = DisplayX + (column * 8);
            for (var pair = 0; pair < 4; pair++)
            {
                var code = (bits >> (6 - (pair * 2))) & 0x03;
                var color = code switch
                {
                    1 => color1,
                    2 => color2,
                    3 => color3,
                    _ => color0
                };
                pixels[(pixelY * Width) + pixelX + (pair * 2)] = color;
                pixels[(pixelY * Width) + pixelX + (pair * 2) + 1] = color;
            }
        }

        private static void RenderSprites(
            Argb32[] pixels,
            byte[] vicRegisters,
            Func<ushort, byte> readVicMemory,
            int vicBankBase,
            byte[]? spritePointers)
        {
            var enabled = vicRegisters[0x15];
            if (enabled == 0)
            {
                return;
            }

            var screenBase = vicBankBase + ((vicRegisters[0x18] & 0xF0) << 6);
            var sharedMulticolor0 = GetColor(vicRegisters[0x25]);
            var sharedMulticolor1 = GetColor(vicRegisters[0x26]);
            for (var sprite = 0; sprite < 8; sprite++)
            {
                var mask = 1 << sprite;
                if ((enabled & mask) == 0)
                {
                    continue;
                }

                var spriteX = vicRegisters[sprite * 2] + (((vicRegisters[0x10] & mask) != 0) ? 256 : 0);
                var spriteY = vicRegisters[(sprite * 2) + 1];
                var originX = DisplayX + spriteX - 24;
                var originY = DisplayY + spriteY - 50;
                var pointer = spritePointers == null
                    ? readVicMemory((ushort)((screenBase + 0x03F8 + sprite) & 0xFFFF))
                    : spritePointers[sprite];
                var dataBase = (vicBankBase + (pointer * 64)) & 0xFFFF;
                var multicolor = (vicRegisters[0x1C] & mask) != 0;
                var expandX = (vicRegisters[0x1D] & mask) != 0;
                var expandY = (vicRegisters[0x17] & mask) != 0;
                var spriteColor = GetColor(vicRegisters[0x27 + sprite]);

                if (multicolor)
                {
                    DrawMulticolorSprite(
                        pixels,
                        readVicMemory,
                        dataBase,
                        originX,
                        originY,
                        expandX,
                        expandY,
                        sharedMulticolor0,
                        spriteColor,
                        sharedMulticolor1);
                }
                else
                {
                    DrawHiresSprite(pixels, readVicMemory, dataBase, originX, originY, expandX, expandY, spriteColor);
                }
            }
        }

        private static void DrawHiresSprite(
            Argb32[] pixels,
            Func<ushort, byte> readVicMemory,
            int dataBase,
            int originX,
            int originY,
            bool expandX,
            bool expandY,
            Argb32 color)
        {
            var pixelScaleX = expandX ? 2 : 1;
            var pixelScaleY = expandY ? 2 : 1;
            for (var row = 0; row < 21; row++)
            {
                var b0 = readVicMemory((ushort)((dataBase + (row * 3)) & 0xFFFF));
                var b1 = readVicMemory((ushort)((dataBase + (row * 3) + 1) & 0xFFFF));
                var b2 = readVicMemory((ushort)((dataBase + (row * 3) + 2) & 0xFFFF));
                var bits = (b0 << 16) | (b1 << 8) | b2;
                for (var bit = 0; bit < 24; bit++)
                {
                    if ((bits & (0x800000 >> bit)) == 0)
                    {
                        continue;
                    }

                    DrawScaledPixel(pixels, originX + (bit * pixelScaleX), originY + (row * pixelScaleY), pixelScaleX, pixelScaleY, color);
                }
            }
        }

        private static void DrawMulticolorSprite(
            Argb32[] pixels,
            Func<ushort, byte> readVicMemory,
            int dataBase,
            int originX,
            int originY,
            bool expandX,
            bool expandY,
            Argb32 sharedMulticolor0,
            Argb32 spriteColor,
            Argb32 sharedMulticolor1)
        {
            var pixelScaleX = expandX ? 4 : 2;
            var pixelScaleY = expandY ? 2 : 1;
            for (var row = 0; row < 21; row++)
            {
                var b0 = readVicMemory((ushort)((dataBase + (row * 3)) & 0xFFFF));
                var b1 = readVicMemory((ushort)((dataBase + (row * 3) + 1) & 0xFFFF));
                var b2 = readVicMemory((ushort)((dataBase + (row * 3) + 2) & 0xFFFF));
                var bits = (b0 << 16) | (b1 << 8) | b2;
                for (var pair = 0; pair < 12; pair++)
                {
                    var code = (bits >> (22 - (pair * 2))) & 0x03;
                    if (code == 0)
                    {
                        continue;
                    }

                    var color = code switch
                    {
                        1 => sharedMulticolor0,
                        2 => spriteColor,
                        _ => sharedMulticolor1
                    };
                    DrawScaledPixel(pixels, originX + (pair * pixelScaleX), originY + (row * pixelScaleY), pixelScaleX, pixelScaleY, color);
                }
            }
        }

        private static void DrawScaledPixel(Argb32[] pixels, int x, int y, int width, int height, Argb32 color)
        {
            for (var yy = 0; yy < height; yy++)
            {
                var py = y + yy;
                if ((uint)py >= Height)
                {
                    continue;
                }

                for (var xx = 0; xx < width; xx++)
                {
                    var px = x + xx;
                    if ((uint)px < Width)
                    {
                        pixels[(py * Width) + px] = color;
                    }
                }
            }
        }

        private static void Fill(Argb32[] pixels, Argb32 color)
        {
            Array.Fill(pixels, color);
        }

        private static Argb32 GetColor(int index)
        {
            return Palette[index & 0x0F];
        }
    }
}
