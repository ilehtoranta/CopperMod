using System;

namespace CopperMod.Sid
{
    internal static class C64KeyboardMatrix
    {
        private static readonly KeyPosition[] Positions =
        {
            new(C64Key.Delete, 0, 0),
            new(C64Key.Return, 0, 1),
            new(C64Key.CursorRight, 0, 2),
            new(C64Key.F7, 0, 3),
            new(C64Key.F1, 0, 4),
            new(C64Key.F3, 0, 5),
            new(C64Key.F5, 0, 6),
            new(C64Key.CursorDown, 0, 7),
            new(C64Key.Three, 1, 0),
            new(C64Key.W, 1, 1),
            new(C64Key.A, 1, 2),
            new(C64Key.Four, 1, 3),
            new(C64Key.Z, 1, 4),
            new(C64Key.S, 1, 5),
            new(C64Key.E, 1, 6),
            new(C64Key.LeftShift, 1, 7),
            new(C64Key.Five, 2, 0),
            new(C64Key.R, 2, 1),
            new(C64Key.D, 2, 2),
            new(C64Key.Six, 2, 3),
            new(C64Key.C, 2, 4),
            new(C64Key.F, 2, 5),
            new(C64Key.T, 2, 6),
            new(C64Key.X, 2, 7),
            new(C64Key.Seven, 3, 0),
            new(C64Key.Y, 3, 1),
            new(C64Key.G, 3, 2),
            new(C64Key.Eight, 3, 3),
            new(C64Key.B, 3, 4),
            new(C64Key.H, 3, 5),
            new(C64Key.U, 3, 6),
            new(C64Key.V, 3, 7),
            new(C64Key.Nine, 4, 0),
            new(C64Key.I, 4, 1),
            new(C64Key.J, 4, 2),
            new(C64Key.Zero, 4, 3),
            new(C64Key.M, 4, 4),
            new(C64Key.K, 4, 5),
            new(C64Key.O, 4, 6),
            new(C64Key.N, 4, 7),
            new(C64Key.Plus, 5, 0),
            new(C64Key.P, 5, 1),
            new(C64Key.L, 5, 2),
            new(C64Key.Minus, 5, 3),
            new(C64Key.Period, 5, 4),
            new(C64Key.Colon, 5, 5),
            new(C64Key.At, 5, 6),
            new(C64Key.Comma, 5, 7),
            new(C64Key.Pound, 6, 0),
            new(C64Key.Asterisk, 6, 1),
            new(C64Key.Semicolon, 6, 2),
            new(C64Key.Home, 6, 3),
            new(C64Key.RightShift, 6, 4),
            new(C64Key.Equals, 6, 5),
            new(C64Key.UpArrow, 6, 6),
            new(C64Key.Slash, 6, 7),
            new(C64Key.One, 7, 0),
            new(C64Key.LeftArrow, 7, 1),
            new(C64Key.Ctrl, 7, 2),
            new(C64Key.Two, 7, 3),
            new(C64Key.Space, 7, 4),
            new(C64Key.Commodore, 7, 5),
            new(C64Key.Q, 7, 6),
            new(C64Key.RunStop, 7, 7)
        };

        private static readonly KeyPosition[] Lookup = BuildLookup();

        public static KeyPosition GetPosition(C64Key key)
        {
            var index = (int)key;
            if ((uint)index >= (uint)Lookup.Length || Lookup[index].Column < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown C64 key.");
            }

            return Lookup[index];
        }

        private static KeyPosition[] BuildLookup()
        {
            var values = (C64Key[])Enum.GetValues(typeof(C64Key));
            var lookup = new KeyPosition[values.Length];
            for (var i = 0; i < lookup.Length; i++)
            {
                lookup[i] = new KeyPosition((C64Key)i, -1, -1);
            }

            foreach (var position in Positions)
            {
                lookup[(int)position.Key] = position;
            }

            return lookup;
        }

        internal readonly struct KeyPosition
        {
            public KeyPosition(C64Key key, int column, int row)
            {
                Key = key;
                Column = column;
                Row = row;
            }

            public C64Key Key { get; }

            public int Column { get; }

            public int Row { get; }

            public byte ColumnMask => (byte)(1 << Column);

            public byte RowMask => (byte)(1 << Row);
        }
    }
}
