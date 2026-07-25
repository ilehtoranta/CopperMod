using Copper68k;
using CopperMod.Amiga;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CopperStart.Devices.Console;
using CopperMod.Amiga.CopperStart.Devices.Input;
using CopperMod.Amiga.CopperStart.Exec;

namespace CopperMod.Amiga.Tests;

public sealed class ConsoleDeviceServicesTests
{
    private const uint ExecBase = 0x3000, Device = 0x3500, Name = 0x3600, Request = 0x3800, Window = 0x3900, RastPort = 0x3A00, Data = 0x3B00, Event = 0x3C00;

    [Fact]
    public void RomConsoleWritesWindowTextAndCompletesPendingInputRead()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        var replies = new List<uint>(); var draws = new List<string>();
        using var console = new ConsoleDeviceServices(bus, memory, input, replies.Add, (state, rastPort, text, length) => draws.Add(string.Concat(Enumerable.Range(0, (int)length).Select(index => (char)bus.ReadByte(text + (uint)index)))));
        Assert.True(console.TryInstall(ExecBase)); Assert.True(bus.HasHostGateway(Device - 30)); Assert.True(bus.HasHostGateway(Device - 42));

        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0; bus.WriteLong(Request + 0x28, Window);
        Assert.True(Invoke(bus, Device - 6, state)); Assert.Equal(Device, bus.ReadLong(Request + 0x14));

        bus.WriteByte(Data, (byte)'H', 10); bus.WriteByte(Data + 1, (byte)'i', 10); bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, 2); bus.WriteByte(Request + 0x1E, 0, 10);
        Assert.True(Invoke(bus, Device - 30, state)); Assert.Equal(new[] { "Hi" }, draws); Assert.Equal(2u, bus.ReadLong(Request + 0x20));

        replies.Clear(); bus.WriteWord(Request + 0x1C, 2); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, 8); bus.WriteByte(Request + 0x1E, 0, 10);
        Assert.True(Invoke(bus, Device - 30, state)); Assert.Empty(replies);
        bus.WriteByte(Event + 4, 1, 10); bus.WriteWord(Event + 6, 0x0020); state.A[0] = Event;
        Assert.True(Invoke(bus, Device - 42, state));
        Assert.Equal((byte)'a', bus.ReadByte(Data)); Assert.Equal(1u, bus.ReadLong(Request + 0x20)); Assert.Equal(new[] { Request }, replies);
    }

    private static void InitializeDevice(AmigaBus bus)
    {
        bus.WriteLong(ExecBase + 0x15E, Device); bus.WriteLong(ExecBase + 0x162, 0); bus.WriteLong(ExecBase + 0x166, Device);
        bus.WriteLong(Device, ExecBase + 0x162); bus.WriteLong(Device + 4, ExecBase + 0x15E); bus.WriteLong(Device + 0x0A, Name);
        foreach (var (index, value) in "console.device\0".Select((value, index) => (index, value))) bus.WriteByte(Name + (uint)index, (byte)value, 0);
    }

    private static bool Invoke(AmigaBus bus, uint address, M68kCpuState state)
        => bus.ReadWord(address) == 0xFF00 && bus.TryInvokeHostGateway(address, bus.ReadLong(address + 2), state);
}
