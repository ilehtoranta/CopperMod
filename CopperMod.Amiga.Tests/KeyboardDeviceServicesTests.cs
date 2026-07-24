using Copper68k;
using CopperMod.Amiga;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CopperStart.Devices.Keyboard;
using CopperMod.Amiga.CopperStart.Exec;
using CopperMod.Amiga.Input;

namespace CopperMod.Amiga.Tests;

public sealed class KeyboardDeviceServicesTests
{
    private const uint ExecBase = 0x3000;
    private const uint Keyboard = 0x3500;
    private const uint Input = 0x3600;
    private const uint KeyboardName = 0x3700;
    private const uint InputName = 0x3720;
    private const uint Request = 0x3800;
    private const uint Buffer = 0x3900;

    [Fact]
    public void DiscoversRomDevicesAndInstallsOnlyDirectGateways()
    {
        var bus = CreateBus(); InitializeDevices(bus);
        using var service = CreateService(bus, out _);
        Assert.True(service.TryInstall(ExecBase));
        Assert.Equal(Keyboard, service.DeviceBase);
        Assert.Equal(Input, service.InputDeviceBase);
        Assert.True(bus.HasHostGateway(Keyboard - 6));
        Assert.True(bus.HasHostGateway(Keyboard - 30));
        Assert.Equal(0xFF00, bus.ReadWord(Keyboard - 30));
    }

    [Fact]
    public void HostRawKeysBypassCiaAndEnterNativeInputBridge()
    {
        var bus = CreateBus(); InitializeDevices(bus);
        using var service = CreateService(bus, out _);
        Assert.True(service.TryInstall(ExecBase));
        var ciaBefore = bus.ReadByte(0x00BFEC01);
        Assert.True(service.QueueKeyDown(AmigaRawKey.LeftShift));
        Assert.True(service.QueueKeyDown(AmigaRawKey.A));
        Assert.True(service.QueueKeyUp(AmigaRawKey.A));
        var state = new M68kCpuState { Cycles = 100, ProgramCounter = 0x2000 };
        state.A[7] = 0x5000;
        service.ProcessPending(state);
        Assert.Equal(Input - 30, state.ProgramCounter);
        Assert.Equal(Input, bus.ReadLong(state.A[1] + 0x14));
        Assert.Equal(11, bus.ReadWord(state.A[1] + 0x1C));
        var inputEvent = bus.ReadLong(state.A[1] + 0x28);
        Assert.Equal(1, bus.ReadByte(inputEvent + 4));
        Assert.Equal((ushort)AmigaRawKey.LeftShift, bus.ReadWord(inputEvent + 6));
        Assert.Equal(1, bus.ReadWord(inputEvent + 8));
        Assert.Equal(ciaBefore, bus.ReadByte(0x00BFEC01));
    }

    [Fact]
    public void ReadEventMatrixAndAbortUseKeyboardState()
    {
        var bus = CreateBus(); InitializeDevices(bus);
        var replies = new List<uint>();
        using var service = CreateService(bus, out _, replies);
        Assert.True(service.TryInstall(ExecBase));
        Open(bus);
        Assert.True(service.QueueKeyDown(AmigaRawKey.A));
        var state = new M68kCpuState { Cycles = 10 };
        state.A[1] = Request;
        bus.WriteWord(Request + 0x1C, 9); bus.WriteLong(Request + 0x28, Buffer); bus.WriteLong(Request + 0x24, 1);
        Assert.True(Invoke(bus, Keyboard - 30, state));
        Assert.Equal((byte)AmigaRawKey.A, bus.ReadByte(Buffer));
        Assert.Equal(1u, bus.ReadLong(Request + 0x20));
        Assert.Equal(new[] { Request }, replies);

        replies.Clear(); bus.WriteWord(Request + 0x1C, 10); bus.WriteLong(Request + 0x28, Buffer); bus.WriteLong(Request + 0x24, 16);
        Assert.True(Invoke(bus, Keyboard - 30, state));
        Assert.NotEqual(0, bus.ReadByte(Buffer + ((uint)AmigaRawKey.A >> 3)) & (1 << ((byte)AmigaRawKey.A & 7)));

        replies.Clear(); bus.WriteWord(Request + 0x1C, 9); bus.WriteLong(Request + 0x28, Buffer); bus.WriteLong(Request + 0x24, 1);
        Assert.True(Invoke(bus, Keyboard - 30, state));
        Assert.True(Invoke(bus, Keyboard - 36, state));
        Assert.Equal(0xFE, bus.ReadByte(Request + 0x1F));
        Assert.Equal(new[] { Request }, replies);
    }

    [Fact]
    public void RomInputTaskReadIsHeldWhileTheDirectInputBridgeDeliversTheKey()
    {
        var bus = CreateBus(); InitializeDevices(bus);
        var replies = new List<uint>();
        using var service = CreateService(bus, out _, replies, () => 0x5000);
        Assert.True(service.TryInstall(ExecBase));
        Open(bus);
        var state = new M68kCpuState { Cycles = 10, ProgramCounter = 0x2200 };
        state.A[1] = Request;
        bus.WriteWord(Request + 0x1C, 9); bus.WriteLong(Request + 0x28, Buffer); bus.WriteLong(Request + 0x24, 1);
        Assert.True(Invoke(bus, Keyboard - 30, state));
        Assert.Empty(replies);

        Assert.True(service.QueueKeyDown(AmigaRawKey.A));
        state.A[7] = 0x5000;
        service.ProcessPending(state);
        Assert.Equal(Input - 30, state.ProgramCounter);
        Assert.Empty(replies);
    }

    private static KeyboardDeviceServices CreateService(AmigaBus bus, out uint scratch, List<uint>? replies = null, Func<uint>? getCurrentTask = null)
    {
        var allocation = 0x4000u;
        scratch = allocation;
        return new KeyboardDeviceServices(bus, new ExecMemoryOperations((_, _) => allocation, (_, _) => { }, (address, length) => bus.ClearMemory(address, length)), (replies ?? new List<uint>()).Add, _ => { }, getCurrentTask ?? (() => 0));
    }

    private static AmigaBus CreateBus() => new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;

    private static void Open(AmigaBus bus)
    {
        var state = new M68kCpuState(); state.A[1] = Request;
        Assert.True(Invoke(bus, Keyboard - 6, state)); Assert.Equal(0u, state.D[0]);
        bus.WriteByte(Request + 0x1E, 0, 0);
    }

    private static void InitializeDevices(AmigaBus bus)
    {
        InitializeList(bus, ExecBase + 0x15E);
        WriteName(bus, KeyboardName, "keyboard.device"); WriteName(bus, InputName, "input.device");
        bus.WriteLong(Keyboard + 0x0A, KeyboardName); bus.WriteLong(Input + 0x0A, InputName);
        bus.WriteLong(Keyboard, Input); bus.WriteLong(Input, ExecBase + 0x15E + 4);
        bus.WriteLong(Input + 4, Keyboard); bus.WriteLong(Keyboard + 4, ExecBase + 0x15E);
        bus.WriteLong(ExecBase + 0x15E, Keyboard); bus.WriteLong(ExecBase + 0x15E + 8, Input);
    }

    private static void InitializeList(AmigaBus bus, uint address) { bus.WriteLong(address, address + 4); bus.WriteLong(address + 4, 0); bus.WriteLong(address + 8, address); }
    private static void WriteName(AmigaBus bus, uint address, string value) { for (var i = 0; i < value.Length; i++) bus.WriteByte(address + (uint)i, (byte)value[i], 0); bus.WriteByte(address + (uint)value.Length, 0, 0); }
    private static bool Invoke(AmigaBus bus, uint address, M68kCpuState state) => bus.ReadWord(address) == 0xFF00 && bus.TryInvokeHostGateway(address, bus.ReadLong(address + 2), state);
}
