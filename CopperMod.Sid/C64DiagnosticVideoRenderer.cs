using System;

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
