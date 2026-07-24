using Copper68k;
using CopperMod.Amiga;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CopperStart.Devices.Gameport;

namespace CopperMod.Amiga.Tests;

public sealed class GameportDeviceServicesTests
{
    private const uint ExecBase = 0x3000, Device = 0x3500, Name = 0x3600, Request = 0x3800, Data = 0x3900;

    [Fact]
    public void RomGameportReadsHardwareMouseCountersAndCompletesPendingRead()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus);
        var replies = new List<uint>();
        using var service = new GameportDeviceServices(bus, replies.Add);
        Assert.True(service.TryInstall(ExecBase));
        Assert.True(bus.HasHostGateway(Device - 30));

        var state = new M68kCpuState { Cycles = 10 };
        state.A[1] = Request; state.D[0] = 0;
        Assert.True(Invoke(bus, Device - 6, state));
        Assert.Equal(Device, bus.ReadLong(Request + 0x14));

        bus.WriteByte(Data, 1, 10); bus.WriteWord(Request + 0x1C, 11); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, 1); bus.WriteByte(Request + 0x1E, 0, 10);
        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal((byte)1, service.ControllerType(0));
        bus.WriteByte(Data, 0, 10); bus.WriteWord(Request + 0x1C, 10);
        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal((byte)1, bus.ReadByte(Data));

        replies.Clear(); bus.WriteWord(Request + 0x1C, 9); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, 0x18); bus.WriteByte(Request + 0x1E, 0, 10);
        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Empty(replies);
        bus.MoveGamePortMouse(0, 3, -2);
        state.Cycles = 20; service.ProcessPending(state);
        Assert.Equal((byte)2, bus.ReadByte(Data + 4));
        Assert.Equal((ushort)3, bus.ReadWord(Data + 0x0A));
        Assert.Equal(unchecked((ushort)-2), bus.ReadWord(Data + 0x0C));
        Assert.Equal(new[] { Request }, replies);
    }

    private static void InitializeDevice(AmigaBus bus)
    {
        bus.WriteLong(ExecBase + 0x15E, Device); bus.WriteLong(ExecBase + 0x162, 0); bus.WriteLong(ExecBase + 0x166, Device);
        bus.WriteLong(Device, ExecBase + 0x162); bus.WriteLong(Device + 4, ExecBase + 0x15E); bus.WriteLong(Device + 0x0A, Name);
        foreach (var (index, value) in "gameport.device\0".Select((value, index) => (index, value))) bus.WriteByte(Name + (uint)index, (byte)value, 0);
    }

    private static bool Invoke(AmigaBus bus, uint address, M68kCpuState state)
        => bus.ReadWord(address) == 0xFF00 && bus.TryInvokeHostGateway(address, bus.ReadLong(address + 2), state);
}
