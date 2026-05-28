using System.Reflection;
using Avalonia.Input;
using CopperMod.Amiga;
using CopperScreen;

namespace CopperScreen.Tests;

public sealed class AmigaHostKeyMapperTests
{
	[Fact]
	public void HostKeyMapperMapsCommonKeysToAmigaRawKeys()
	{
		Assert.True(AmigaHostKeyMapper.TryMap(Key.Space, PhysicalKey.Space, NumpadInputMode.Joystick, out var space));
		Assert.Equal(AmigaRawKey.Space, space);

		Assert.True(AmigaHostKeyMapper.TryMap(Key.Return, PhysicalKey.Enter, NumpadInputMode.Joystick, out var enter));
		Assert.Equal(AmigaRawKey.Return, enter);

		Assert.True(AmigaHostKeyMapper.TryMap(Key.F1, PhysicalKey.F1, NumpadInputMode.Joystick, out var f1));
		Assert.Equal(AmigaRawKey.F1, f1);
	}

	[Fact]
	public void HostKeyMapperReservesToolbarDiskAndNumLockKeys()
	{
		Assert.False(AmigaHostKeyMapper.TryMap(Key.F11, PhysicalKey.F11, NumpadInputMode.Joystick, out _));
		Assert.False(AmigaHostKeyMapper.TryMap(Key.F12, PhysicalKey.F12, NumpadInputMode.Joystick, out _));
		Assert.False(AmigaHostKeyMapper.TryMap(Key.NumLock, PhysicalKey.NumLock, NumpadInputMode.Joystick, out _));
	}

	[Fact]
	public void HostKeyMapperUsesNumLockModeForNumpad()
	{
		Assert.False(AmigaHostKeyMapper.TryMap(Key.Down, PhysicalKey.NumPad2, NumpadInputMode.Joystick, out _));

		Assert.True(AmigaHostKeyMapper.TryMap(Key.Down, PhysicalKey.NumPad2, NumpadInputMode.AmigaKeys, out var rawKey));
		Assert.Equal(AmigaRawKey.NumPad2, rawKey);
	}

	[Fact]
	public void EmulatorKeyboardEventsReachCiaASerialRegister()
	{
		var emulator = CopperScreenEmulator.CreateWithoutDisk();
		var machine = GetMachine(emulator);
		machine.Bus.AbleCiaInterrupts(AmigaCiaId.A, 0x80 | AmigaCia.SerialInterruptMask, 0);

		emulator.KeyDown(AmigaRawKey.A);

		Assert.Equal((byte)AmigaRawKey.A, AmigaKeyboard.DecodeSerialData(machine.Bus.ReadByte(0x00BFEC01)));
		Assert.Contains(machine.Bus.DrainCiaInterrupts(), interruptEvent =>
			interruptEvent.Cia == AmigaCiaId.A &&
			interruptEvent.IcrBits == AmigaCia.SerialInterruptMask);
	}

	private static AmigaMachine GetMachine(CopperScreenEmulator emulator)
	{
		return (AmigaMachine)typeof(CopperScreenEmulator)
			.GetField("_machine", BindingFlags.NonPublic | BindingFlags.Instance)!
			.GetValue(emulator)!;
	}
}
