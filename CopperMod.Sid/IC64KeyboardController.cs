#pragma warning disable CS1591

namespace CopperMod.Sid
{
    public interface IC64KeyboardController
    {
        void SetKeyPressed(C64Key key, bool pressed);

        void ReleaseAllKeys();
    }

    public enum C64Key
    {
        Delete,
        Return,
        CursorRight,
        F7,
        F1,
        F3,
        F5,
        CursorDown,
        Three,
        W,
        A,
        Four,
        Z,
        S,
        E,
        LeftShift,
        Five,
        R,
        D,
        Six,
        C,
        F,
        T,
        X,
        Seven,
        Y,
        G,
        Eight,
        B,
        H,
        U,
        V,
        Nine,
        I,
        J,
        Zero,
        M,
        K,
        O,
        N,
        Plus,
        P,
        L,
        Minus,
        Period,
        Colon,
        At,
        Comma,
        Pound,
        Asterisk,
        Semicolon,
        Home,
        RightShift,
        Equals,
        UpArrow,
        Slash,
        One,
        LeftArrow,
        Ctrl,
        Two,
        Space,
        Commodore,
        Q,
        RunStop
    }
}

#pragma warning restore CS1591
