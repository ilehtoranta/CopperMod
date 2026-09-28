using Amiga;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using System.Runtime.InteropServices;

namespace CopperMod.Amiga.Tests;

public sealed partial class KickstartRomLayersDifferentialTests
{
    private static ClassicTextFontFixture CreateClassicTextFontFixture(OracleContext context)
        => new(context);

    /// <summary>
    /// A caller-owned classic bitmap font shared by both execution oracles.
    /// Detach it from every RastPort before disposal; this fixture never adds
    /// a font to, or removes a font from, the system font list.
    /// </summary>
    private sealed class ClassicTextFontFixture : IDisposable
    {
        internal const int Width = 8;
        internal const int Height = 8;
        internal const int Baseline = 7;
        private const uint GlyphCount = 3; // A, B, then the mandatory default slot.
        private const uint Modulo = 4; // Three glyph bytes plus even-row padding.
        private const uint StrikeBytes = Modulo * Height;
        private const uint LocationBytes = GlyphCount * 4;
        private const uint SpacingBytes = GlyphCount * 2;
        private const uint TableBytes = LocationBytes + SpacingBytes * 2;
        private readonly OracleContext _context;
        private readonly uint _fontBytes;
        private uint _tables;
        private uint _strike;

        private static ReadOnlySpan<byte> Name => "layers-oracle.font\0"u8;
        private static ReadOnlySpan<byte> GlyphA =>
            [0x18, 0x24, 0x42, 0x42, 0x7E, 0x42, 0x42, 0x00];
        private static ReadOnlySpan<byte> GlyphB =>
            [0x7C, 0x42, 0x42, 0x7C, 0x42, 0x42, 0x7C, 0x00];
        private static ReadOnlySpan<byte> DefaultGlyph =>
            [0x7E, 0x42, 0x5A, 0x5A, 0x42, 0x5A, 0x7E, 0x00];

        internal uint Font { get; private set; }

        // Expected pixels use these source rows, not guest decoding or the
        // graphics implementation under comparison.
        internal ReadOnlySpan<byte> GlyphRows(byte character) => character switch
        {
            (byte)'A' => GlyphA,
            (byte)'B' => GlyphB,
            _ => DefaultGlyph
        };

        internal ClassicTextFontFixture(OracleContext context)
        {
            _context = context;
            _fontBytes = checked(TextFont.Size + (uint)Name.Length);
            try
            {
                Font = context.Allocate(_fontBytes);
                _tables = context.Allocate(TableBytes);
                _strike = context.Allocate(StrikeBytes, Exec.MemoryFlags.Chip);
                Assert.Equal(_strike, context.Bus.MaskChipDmaAddress(_strike));
                Assert.InRange(_strike, 1u,
                    checked((uint)context.Machine.Options.ChipRamSize - StrikeBytes));

                var bus = context.Bus;
                var message = Field<TextFont>(Font, nameof(TextFont.Message));
                var node = Field<Message>(message, nameof(Message.Node));
                var name = Font + TextFont.Size;
                bus.WriteByte(Field<Node>(node, nameof(Node.Type)), (byte)NodeType.Font, 0);
                bus.WriteLong(Field<Node>(node, nameof(Node.Name)), name);
                for (var index = 0; index < Name.Length; index++)
                    bus.WriteByte(name + checked((uint)index), Name[index], 0);

                bus.WriteWord(Field<TextFont>(Font, nameof(TextFont.YSize)), Height);
                bus.WriteByte(Field<TextFont>(Font, nameof(TextFont.Style)), 0, 0);
                bus.WriteByte(Field<TextFont>(Font, nameof(TextFont.Flags)),
                    (byte)FontFlags.Designed, 0);
                bus.WriteWord(Field<TextFont>(Font, nameof(TextFont.XSize)), Width);
                bus.WriteWord(Field<TextFont>(Font, nameof(TextFont.Baseline)), Baseline);
                bus.WriteWord(Field<TextFont>(Font, nameof(TextFont.BoldSmear)), 1);
                bus.WriteByte(Field<TextFont>(Font, nameof(TextFont.LoChar)), (byte)'A', 0);
                bus.WriteByte(Field<TextFont>(Font, nameof(TextFont.HiChar)), (byte)'B', 0);
                bus.WriteLong(Field<TextFont>(Font, nameof(TextFont.CharData)), _strike);
                bus.WriteWord(Field<TextFont>(Font, nameof(TextFont.Modulo)), (ushort)Modulo);
                bus.WriteLong(Field<TextFont>(Font, nameof(TextFont.CharLoc)), _tables);
                bus.WriteLong(Field<TextFont>(Font, nameof(TextFont.CharSpace)),
                    _tables + LocationBytes);
                bus.WriteLong(Field<TextFont>(Font, nameof(TextFont.CharKern)),
                    _tables + LocationBytes + SpacingBytes);

                // Classic CharLoc stores two WORDs: strike bit offset and width.
                // Both Space and Kern are explicit, avoiding the mixed-null
                // variants which V36 SetFont repairs by changing the font.
                // https://wiki.amigaos.net/wiki/Graphics_Library_and_Text
                // https://d0.se/autodocs/graphics.library/SetFont
                for (var glyph = 0u; glyph < GlyphCount; glyph++)
                {
                    bus.WriteWord(_tables + glyph * 4, checked((ushort)(glyph * Width)));
                    bus.WriteWord(_tables + glyph * 4 + 2, Width);
                    bus.WriteWord(_tables + LocationBytes + glyph * 2, Width);
                    bus.WriteWord(_tables + LocationBytes + SpacingBytes + glyph * 2, 0);
                    var rows = GlyphRows(glyph switch
                    {
                        0 => (byte)'A',
                        1 => (byte)'B',
                        _ => (byte)'?'
                    });
                    for (var row = 0; row < Height; row++)
                        bus.WriteByte(_strike + checked((uint)row) * Modulo + glyph,
                            rows[row], 0);
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (Font == 0)
                return;
            _context.WaitForBlitterIdle();
            Assert.False(_context.Bus.Blitter.Busy);

            // V36+ SetFont may attach a 24-byte extension to an in-code font.
            // Release library-owned state through its public API, never FreeMem
            // its pointer or assume a private allocation extent.
            // https://d0.se/autodocs/graphics.library/SetFont
            // https://d0.se/autodocs/graphics.library/StripFont
            var extensionField = Field<Message>(
                Field<TextFont>(Font, nameof(TextFont.Message)), nameof(Message.ReplyPort));
            if (_context.Bus.ReadLong(extensionField) != 0)
            {
                _context.InvokeGraphics((int)GraphicsLvo.StripFont,
                    state => state.A[0] = Font);
                Assert.Equal(0u, _context.Bus.ReadLong(extensionField));
            }
            if (_strike != 0)
            {
                _context.Free(_strike, StrikeBytes);
                _strike = 0;
            }
            if (_tables != 0)
            {
                _context.Free(_tables, TableBytes);
                _tables = 0;
            }
            _context.Free(Font, _fontBytes);
            Font = 0;
        }

        private static uint Field<T>(uint address, string field) where T : struct
            => checked(address + (uint)Marshal.OffsetOf<T>(field).ToInt32());
    }
}
