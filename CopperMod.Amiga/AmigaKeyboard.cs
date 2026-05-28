using System;
using System.Collections.Generic;

namespace CopperMod.Amiga
{
    internal enum AmigaRawKey : byte
    {
        Backquote = 0x00,
        Digit1 = 0x01,
        Digit2 = 0x02,
        Digit3 = 0x03,
        Digit4 = 0x04,
        Digit5 = 0x05,
        Digit6 = 0x06,
        Digit7 = 0x07,
        Digit8 = 0x08,
        Digit9 = 0x09,
        Digit0 = 0x0A,
        Minus = 0x0B,
        Equal = 0x0C,
        Backslash = 0x0D,
        NumPad0 = 0x0F,
        Q = 0x10,
        W = 0x11,
        E = 0x12,
        R = 0x13,
        T = 0x14,
        Y = 0x15,
        U = 0x16,
        I = 0x17,
        O = 0x18,
        P = 0x19,
        BracketLeft = 0x1A,
        BracketRight = 0x1B,
        NumPad1 = 0x1D,
        NumPad2 = 0x1E,
        NumPad3 = 0x1F,
        A = 0x20,
        S = 0x21,
        D = 0x22,
        F = 0x23,
        G = 0x24,
        H = 0x25,
        J = 0x26,
        K = 0x27,
        L = 0x28,
        Semicolon = 0x29,
        Quote = 0x2A,
        IntlNearReturn = 0x2B,
        NumPad4 = 0x2D,
        NumPad5 = 0x2E,
        NumPad6 = 0x2F,
        IntlNearLeftShift = 0x30,
        Z = 0x31,
        X = 0x32,
        C = 0x33,
        V = 0x34,
        B = 0x35,
        N = 0x36,
        M = 0x37,
        Comma = 0x38,
        Period = 0x39,
        Slash = 0x3A,
        NumPadDecimal = 0x3C,
        NumPad7 = 0x3D,
        NumPad8 = 0x3E,
        NumPad9 = 0x3F,
        Space = 0x40,
        Backspace = 0x41,
        Tab = 0x42,
        NumPadEnter = 0x43,
        Return = 0x44,
        Escape = 0x45,
        Delete = 0x46,
        Insert = 0x47,
        PageUp = 0x48,
        PageDown = 0x49,
        NumPadSubtract = 0x4A,
        CursorUp = 0x4C,
        CursorDown = 0x4D,
        CursorRight = 0x4E,
        CursorLeft = 0x4F,
        F1 = 0x50,
        F2 = 0x51,
        F3 = 0x52,
        F4 = 0x53,
        F5 = 0x54,
        F6 = 0x55,
        F7 = 0x56,
        F8 = 0x57,
        F9 = 0x58,
        F10 = 0x59,
        NumPadLeftParen = 0x5A,
        NumPadRightParen = 0x5B,
        NumPadDivide = 0x5C,
        NumPadMultiply = 0x5D,
        NumPadAdd = 0x5E,
        Help = 0x5F,
        LeftShift = 0x60,
        RightShift = 0x61,
        CapsLock = 0x62,
        Control = 0x63,
        LeftAlt = 0x64,
        RightAlt = 0x65,
        LeftAmiga = 0x66,
        RightAmiga = 0x67
    }

    internal readonly struct AmigaKeyboardSnapshot
    {
        public AmigaKeyboardSnapshot(byte lastRawKey, int queuedRawKeys, IReadOnlyList<byte> pressedRawKeys)
        {
            LastRawKey = lastRawKey;
            QueuedRawKeys = queuedRawKeys;
            PressedRawKeys = pressedRawKeys;
        }

        public byte LastRawKey { get; }

        public int QueuedRawKeys { get; }

        public IReadOnlyList<byte> PressedRawKeys { get; }
    }

    internal sealed class AmigaKeyboard
    {
        private const byte ReleaseMask = 0x80;
        private readonly Queue<byte> _queue = new Queue<byte>();
        private readonly HashSet<byte> _pressed = new HashSet<byte>();
        private readonly Action<byte, long> _deliver;
        private bool _waitingForGuestRead;
        private byte _lastRawKey;

        public AmigaKeyboard(Action<byte, long> deliver)
        {
            _deliver = deliver ?? throw new ArgumentNullException(nameof(deliver));
        }

        public byte LastRawKey => _lastRawKey;

        public int QueuedRawKeys => _queue.Count + (_waitingForGuestRead ? 1 : 0);

        public static byte EncodeSerialData(byte rawKey)
        {
            return (byte)~((rawKey << 1) | (rawKey >> 7));
        }

        public static byte DecodeSerialData(byte serialData)
        {
            var inverted = (byte)~serialData;
            return (byte)((inverted >> 1) | (inverted << 7));
        }

        public AmigaKeyboardSnapshot CaptureSnapshot()
        {
            var pressed = new byte[_pressed.Count];
            _pressed.CopyTo(pressed);
            Array.Sort(pressed);
            return new AmigaKeyboardSnapshot(_lastRawKey, QueuedRawKeys, pressed);
        }

        public void Reset()
        {
            _queue.Clear();
            _pressed.Clear();
            _waitingForGuestRead = false;
            _lastRawKey = 0;
        }

        public void KeyDown(AmigaRawKey key, long cycle = 0)
        {
            var raw = (byte)key;
            if (raw >= ReleaseMask || !_pressed.Add(raw))
            {
                return;
            }

            Enqueue(raw, cycle);
        }

        public void KeyUp(AmigaRawKey key, long cycle = 0)
        {
            var raw = (byte)key;
            if (raw >= ReleaseMask || !_pressed.Remove(raw))
            {
                return;
            }

            Enqueue((byte)(raw | ReleaseMask), cycle);
        }

        public void AcknowledgeSerialDataRead(long cycle)
        {
            if (!_waitingForGuestRead)
            {
                return;
            }

            _waitingForGuestRead = false;
            TryDeliverNext(cycle);
        }

        private void Enqueue(byte rawKey, long cycle)
        {
            _queue.Enqueue(rawKey);
            TryDeliverNext(cycle);
        }

        private void TryDeliverNext(long cycle)
        {
            if (_waitingForGuestRead || _queue.Count == 0)
            {
                return;
            }

            _lastRawKey = _queue.Dequeue();
            _waitingForGuestRead = true;
            _deliver(EncodeSerialData(_lastRawKey), cycle);
        }
    }
}
