using Avalonia.Input;
using CopperMod.Amiga;

namespace CopperScreen;

internal enum NumpadInputMode
{
	Joystick,
	AmigaKeys
}

internal static class AmigaHostKeyMapper
{
	public static bool TryMap(Key key, PhysicalKey physicalKey, NumpadInputMode numpadMode, out AmigaRawKey rawKey)
	{
		if (IsReservedHostKey(key, physicalKey))
		{
			rawKey = default;
			return false;
		}

		if (physicalKey != PhysicalKey.None)
		{
			return TryMapPhysicalKey(physicalKey, numpadMode, out rawKey);
		}

		return TryMapLogicalKey(key, numpadMode, out rawKey);
	}

	private static bool IsReservedHostKey(Key key, PhysicalKey physicalKey)
	{
		return key is Key.F11 or Key.F12 or Key.NumLock ||
			physicalKey is PhysicalKey.F11 or PhysicalKey.F12 or PhysicalKey.NumLock;
	}

	private static bool TryMapPhysicalKey(PhysicalKey key, NumpadInputMode numpadMode, out AmigaRawKey rawKey)
	{
		rawKey = key switch
		{
			PhysicalKey.Backquote => AmigaRawKey.Backquote,
			PhysicalKey.Digit1 => AmigaRawKey.Digit1,
			PhysicalKey.Digit2 => AmigaRawKey.Digit2,
			PhysicalKey.Digit3 => AmigaRawKey.Digit3,
			PhysicalKey.Digit4 => AmigaRawKey.Digit4,
			PhysicalKey.Digit5 => AmigaRawKey.Digit5,
			PhysicalKey.Digit6 => AmigaRawKey.Digit6,
			PhysicalKey.Digit7 => AmigaRawKey.Digit7,
			PhysicalKey.Digit8 => AmigaRawKey.Digit8,
			PhysicalKey.Digit9 => AmigaRawKey.Digit9,
			PhysicalKey.Digit0 => AmigaRawKey.Digit0,
			PhysicalKey.Minus => AmigaRawKey.Minus,
			PhysicalKey.Equal => AmigaRawKey.Equal,
			PhysicalKey.Backslash => AmigaRawKey.Backslash,
			PhysicalKey.Q => AmigaRawKey.Q,
			PhysicalKey.W => AmigaRawKey.W,
			PhysicalKey.E => AmigaRawKey.E,
			PhysicalKey.R => AmigaRawKey.R,
			PhysicalKey.T => AmigaRawKey.T,
			PhysicalKey.Y => AmigaRawKey.Y,
			PhysicalKey.U => AmigaRawKey.U,
			PhysicalKey.I => AmigaRawKey.I,
			PhysicalKey.O => AmigaRawKey.O,
			PhysicalKey.P => AmigaRawKey.P,
			PhysicalKey.BracketLeft => AmigaRawKey.BracketLeft,
			PhysicalKey.BracketRight => AmigaRawKey.BracketRight,
			PhysicalKey.A => AmigaRawKey.A,
			PhysicalKey.S => AmigaRawKey.S,
			PhysicalKey.D => AmigaRawKey.D,
			PhysicalKey.F => AmigaRawKey.F,
			PhysicalKey.G => AmigaRawKey.G,
			PhysicalKey.H => AmigaRawKey.H,
			PhysicalKey.J => AmigaRawKey.J,
			PhysicalKey.K => AmigaRawKey.K,
			PhysicalKey.L => AmigaRawKey.L,
			PhysicalKey.Semicolon => AmigaRawKey.Semicolon,
			PhysicalKey.Quote => AmigaRawKey.Quote,
			PhysicalKey.IntlYen => AmigaRawKey.IntlNearReturn,
			PhysicalKey.IntlBackslash => AmigaRawKey.IntlNearLeftShift,
			PhysicalKey.IntlRo => AmigaRawKey.IntlNearLeftShift,
			PhysicalKey.Z => AmigaRawKey.Z,
			PhysicalKey.X => AmigaRawKey.X,
			PhysicalKey.C => AmigaRawKey.C,
			PhysicalKey.V => AmigaRawKey.V,
			PhysicalKey.B => AmigaRawKey.B,
			PhysicalKey.N => AmigaRawKey.N,
			PhysicalKey.M => AmigaRawKey.M,
			PhysicalKey.Comma => AmigaRawKey.Comma,
			PhysicalKey.Period => AmigaRawKey.Period,
			PhysicalKey.Slash => AmigaRawKey.Slash,
			PhysicalKey.Space => AmigaRawKey.Space,
			PhysicalKey.Backspace => AmigaRawKey.Backspace,
			PhysicalKey.Tab => AmigaRawKey.Tab,
			PhysicalKey.Enter => AmigaRawKey.Return,
			PhysicalKey.Escape => AmigaRawKey.Escape,
			PhysicalKey.Delete => AmigaRawKey.Delete,
			PhysicalKey.Insert => AmigaRawKey.Insert,
			PhysicalKey.PageUp => AmigaRawKey.PageUp,
			PhysicalKey.PageDown => AmigaRawKey.PageDown,
			PhysicalKey.ArrowUp => AmigaRawKey.CursorUp,
			PhysicalKey.ArrowDown => AmigaRawKey.CursorDown,
			PhysicalKey.ArrowRight => AmigaRawKey.CursorRight,
			PhysicalKey.ArrowLeft => AmigaRawKey.CursorLeft,
			PhysicalKey.F1 => AmigaRawKey.F1,
			PhysicalKey.F2 => AmigaRawKey.F2,
			PhysicalKey.F3 => AmigaRawKey.F3,
			PhysicalKey.F4 => AmigaRawKey.F4,
			PhysicalKey.F5 => AmigaRawKey.F5,
			PhysicalKey.F6 => AmigaRawKey.F6,
			PhysicalKey.F7 => AmigaRawKey.F7,
			PhysicalKey.F8 => AmigaRawKey.F8,
			PhysicalKey.F9 => AmigaRawKey.F9,
			PhysicalKey.F10 => AmigaRawKey.F10,
			PhysicalKey.Help => AmigaRawKey.Help,
			PhysicalKey.PrintScreen => AmigaRawKey.Help,
			PhysicalKey.ShiftLeft => AmigaRawKey.LeftShift,
			PhysicalKey.ShiftRight => AmigaRawKey.RightShift,
			PhysicalKey.CapsLock => AmigaRawKey.CapsLock,
			PhysicalKey.ControlLeft => AmigaRawKey.Control,
			PhysicalKey.ControlRight => AmigaRawKey.Control,
			PhysicalKey.AltLeft => AmigaRawKey.LeftAlt,
			PhysicalKey.AltRight => AmigaRawKey.RightAlt,
			PhysicalKey.MetaLeft => AmigaRawKey.LeftAmiga,
			PhysicalKey.MetaRight => AmigaRawKey.RightAmiga,
			PhysicalKey.ContextMenu => AmigaRawKey.RightAmiga,
			_ => default
		};
		if (rawKey != default || key == PhysicalKey.Backquote)
		{
			return true;
		}

		if (numpadMode != NumpadInputMode.AmigaKeys)
		{
			return false;
		}

		rawKey = key switch
		{
			PhysicalKey.NumPad0 => AmigaRawKey.NumPad0,
			PhysicalKey.NumPad1 => AmigaRawKey.NumPad1,
			PhysicalKey.NumPad2 => AmigaRawKey.NumPad2,
			PhysicalKey.NumPad3 => AmigaRawKey.NumPad3,
			PhysicalKey.NumPad4 => AmigaRawKey.NumPad4,
			PhysicalKey.NumPad5 => AmigaRawKey.NumPad5,
			PhysicalKey.NumPadClear => AmigaRawKey.NumPad5,
			PhysicalKey.NumPad6 => AmigaRawKey.NumPad6,
			PhysicalKey.NumPad7 => AmigaRawKey.NumPad7,
			PhysicalKey.NumPad8 => AmigaRawKey.NumPad8,
			PhysicalKey.NumPad9 => AmigaRawKey.NumPad9,
			PhysicalKey.NumPadDecimal => AmigaRawKey.NumPadDecimal,
			PhysicalKey.NumPadComma => AmigaRawKey.NumPadDecimal,
			PhysicalKey.NumPadSubtract => AmigaRawKey.NumPadSubtract,
			PhysicalKey.NumPadEnter => AmigaRawKey.NumPadEnter,
			PhysicalKey.NumPadParenLeft => AmigaRawKey.NumPadLeftParen,
			PhysicalKey.NumPadParenRight => AmigaRawKey.NumPadRightParen,
			PhysicalKey.NumPadDivide => AmigaRawKey.NumPadDivide,
			PhysicalKey.NumPadMultiply => AmigaRawKey.NumPadMultiply,
			PhysicalKey.NumPadAdd => AmigaRawKey.NumPadAdd,
			_ => default
		};

		return rawKey != default;
	}

	private static bool TryMapLogicalKey(Key key, NumpadInputMode numpadMode, out AmigaRawKey rawKey)
	{
		rawKey = key switch
		{
			Key.D1 => AmigaRawKey.Digit1,
			Key.D2 => AmigaRawKey.Digit2,
			Key.D3 => AmigaRawKey.Digit3,
			Key.D4 => AmigaRawKey.Digit4,
			Key.D5 => AmigaRawKey.Digit5,
			Key.D6 => AmigaRawKey.Digit6,
			Key.D7 => AmigaRawKey.Digit7,
			Key.D8 => AmigaRawKey.Digit8,
			Key.D9 => AmigaRawKey.Digit9,
			Key.D0 => AmigaRawKey.Digit0,
			Key.A => AmigaRawKey.A,
			Key.B => AmigaRawKey.B,
			Key.C => AmigaRawKey.C,
			Key.D => AmigaRawKey.D,
			Key.E => AmigaRawKey.E,
			Key.F => AmigaRawKey.F,
			Key.G => AmigaRawKey.G,
			Key.H => AmigaRawKey.H,
			Key.I => AmigaRawKey.I,
			Key.J => AmigaRawKey.J,
			Key.K => AmigaRawKey.K,
			Key.L => AmigaRawKey.L,
			Key.M => AmigaRawKey.M,
			Key.N => AmigaRawKey.N,
			Key.O => AmigaRawKey.O,
			Key.P => AmigaRawKey.P,
			Key.Q => AmigaRawKey.Q,
			Key.R => AmigaRawKey.R,
			Key.S => AmigaRawKey.S,
			Key.T => AmigaRawKey.T,
			Key.U => AmigaRawKey.U,
			Key.V => AmigaRawKey.V,
			Key.W => AmigaRawKey.W,
			Key.X => AmigaRawKey.X,
			Key.Y => AmigaRawKey.Y,
			Key.Z => AmigaRawKey.Z,
			Key.Space => AmigaRawKey.Space,
			Key.Back => AmigaRawKey.Backspace,
			Key.Tab => AmigaRawKey.Tab,
			Key.Enter or Key.Return => AmigaRawKey.Return,
			Key.Escape => AmigaRawKey.Escape,
			Key.Delete => AmigaRawKey.Delete,
			Key.Insert => AmigaRawKey.Insert,
			Key.PageUp => AmigaRawKey.PageUp,
			Key.PageDown => AmigaRawKey.PageDown,
			Key.Up => AmigaRawKey.CursorUp,
			Key.Down => AmigaRawKey.CursorDown,
			Key.Right => AmigaRawKey.CursorRight,
			Key.Left => AmigaRawKey.CursorLeft,
			Key.OemTilde or Key.Oem3 => AmigaRawKey.Backquote,
			Key.OemMinus => AmigaRawKey.Minus,
			Key.OemPlus => AmigaRawKey.Equal,
			Key.OemOpenBrackets or Key.Oem4 => AmigaRawKey.BracketLeft,
			Key.OemCloseBrackets or Key.Oem6 => AmigaRawKey.BracketRight,
			Key.OemPipe or Key.Oem5 => AmigaRawKey.Backslash,
			Key.OemSemicolon or Key.Oem1 => AmigaRawKey.Semicolon,
			Key.OemQuotes or Key.Oem7 => AmigaRawKey.Quote,
			Key.OemComma => AmigaRawKey.Comma,
			Key.OemPeriod => AmigaRawKey.Period,
			Key.OemQuestion or Key.Oem2 => AmigaRawKey.Slash,
			Key.Oem102 or Key.OemBackslash => AmigaRawKey.IntlNearLeftShift,
			Key.F1 => AmigaRawKey.F1,
			Key.F2 => AmigaRawKey.F2,
			Key.F3 => AmigaRawKey.F3,
			Key.F4 => AmigaRawKey.F4,
			Key.F5 => AmigaRawKey.F5,
			Key.F6 => AmigaRawKey.F6,
			Key.F7 => AmigaRawKey.F7,
			Key.F8 => AmigaRawKey.F8,
			Key.F9 => AmigaRawKey.F9,
			Key.F10 => AmigaRawKey.F10,
			Key.Help or Key.PrintScreen => AmigaRawKey.Help,
			Key.LeftShift => AmigaRawKey.LeftShift,
			Key.RightShift => AmigaRawKey.RightShift,
			Key.CapsLock or Key.Capital => AmigaRawKey.CapsLock,
			Key.LeftCtrl or Key.RightCtrl => AmigaRawKey.Control,
			Key.LeftAlt => AmigaRawKey.LeftAlt,
			Key.RightAlt => AmigaRawKey.RightAlt,
			Key.LWin => AmigaRawKey.LeftAmiga,
			Key.RWin or Key.Apps => AmigaRawKey.RightAmiga,
			_ => default
		};
		if (rawKey != default || key == Key.OemTilde || key == Key.Oem3)
		{
			return true;
		}

		if (numpadMode != NumpadInputMode.AmigaKeys)
		{
			return false;
		}

		rawKey = key switch
		{
			Key.NumPad0 => AmigaRawKey.NumPad0,
			Key.NumPad1 => AmigaRawKey.NumPad1,
			Key.NumPad2 => AmigaRawKey.NumPad2,
			Key.NumPad3 => AmigaRawKey.NumPad3,
			Key.NumPad4 => AmigaRawKey.NumPad4,
			Key.NumPad5 or Key.Clear => AmigaRawKey.NumPad5,
			Key.NumPad6 => AmigaRawKey.NumPad6,
			Key.NumPad7 => AmigaRawKey.NumPad7,
			Key.NumPad8 => AmigaRawKey.NumPad8,
			Key.NumPad9 => AmigaRawKey.NumPad9,
			Key.Decimal or Key.Separator => AmigaRawKey.NumPadDecimal,
			Key.Subtract => AmigaRawKey.NumPadSubtract,
			Key.Divide => AmigaRawKey.NumPadDivide,
			Key.Multiply => AmigaRawKey.NumPadMultiply,
			Key.Add => AmigaRawKey.NumPadAdd,
			_ => default
		};

		return rawKey != default;
	}
}
