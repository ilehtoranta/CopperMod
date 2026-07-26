using Copper68k;
using CopperMod.Amiga;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CopperStart.Devices.Audio;

namespace CopperMod.Amiga.Tests;

public sealed class AudioDeviceServicesTests
{
    private const uint ExecBase = 0x3000;
    private const uint Device = 0x3500;
    private const uint Name = 0x3600;
    private const uint FirstRequest = 0x3700;
    private const uint SecondRequest = 0x3800;
    private const uint Choices = 0x3900;

    [Fact]
    public void DiscoversRomDeviceInstallsOnlyDirectGatewaysAndRestoresThemOnReset()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeLiveAudioDevice(bus);
        using var service = new AudioDeviceServices(bus, _ => { }, _ => { });
        Assert.True(service.TryInstall(ExecBase));
        Assert.Equal(Device, service.DeviceBase);
        Assert.Equal(6, service.GatewayRegistrationCount);
        Assert.True(bus.HasHostGateway(Device - 30));
        service.Reset();
        Assert.False(bus.HasHostGateway(Device - 30));
    }

    [Fact]
    public void AllocateUsesPreferredMasksAllocationKeysAndPriorityStealingWithoutPaulaWrites()
    {
        var machine = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false));
        var bus = machine.Bus;
        InitializeLiveAudioDevice(bus);
        using var service = new AudioDeviceServices(bus, _ => { }, _ => { });
        Assert.True(service.TryInstall(ExecBase));
        var state = new M68kCpuState { A = { [1] = FirstRequest } };
        Assert.True(Invoke(bus, Device - 6, state));

        bus.WriteByte(Choices, 3, 0); // prefer channels 0+1
        ConfigureAllocation(bus, FirstRequest, Choices, priority: 10);
        Assert.True(Invoke(bus, Device - 30, state));
        var key = bus.ReadWord(FirstRequest + 0x28);
        Assert.NotEqual((ushort)0, key);
        Assert.Equal(3u, bus.ReadLong(FirstRequest + 0x18));

        state.A[1] = SecondRequest;
        bus.WriteByte(Choices + 1, 1, 0);
        ConfigureAllocation(bus, SecondRequest, Choices + 1, priority: 10, nowait: true);
        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal(0xF9, bus.ReadByte(SecondRequest + 0x1F));

        ConfigureAllocation(bus, SecondRequest, Choices + 1, priority: 11);
        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal(1u, bus.ReadLong(SecondRequest + 0x18));
        // The service never writes custom audio registers during arbitration.
        Assert.Empty(machine.Bus.Paula.Writes);
    }

    [Fact]
    public void WriteCompletesAtDeterministicMachineCycleWithoutProgrammingPaula()
    {
        var machine = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false));
        var bus = machine.Bus; var replies = new List<uint>();
        InitializeLiveAudioDevice(bus);
        using var service = new AudioDeviceServices(bus, replies.Add, _ => { });
        Assert.True(service.TryInstall(ExecBase));
        var state = new M68kCpuState { A = { [1] = FirstRequest } };
        bus.WriteByte(Choices, 1, 0); ConfigureAllocation(bus, FirstRequest, Choices, priority: 1);
        Assert.True(Invoke(bus, Device - 30, state));
        bus.WriteByte(0x3A00, 0x10, 0); bus.WriteByte(0x3A01, 0xF0, 0);
        bus.WriteWord(FirstRequest + 0x1C, 3); bus.WriteLong(FirstRequest + 0x2A, 0x3A00); bus.WriteLong(FirstRequest + 0x2E, 2);
        bus.WriteWord(FirstRequest + 0x32, 10); bus.WriteWord(FirstRequest + 0x34, 64); bus.WriteWord(FirstRequest + 0x36, 2);
        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Empty(replies);
        var mixed = new float[2]; service.MixSample(1, mixed, 0, 2);
        Assert.NotEqual(0f, mixed[1]);
        state.Cycles = 1_000; service.ProcessPending(state);
        Assert.Equal(new[] { FirstRequest }, replies);
        Assert.Equal(0, bus.ReadByte(FirstRequest + 0x1F));
        Assert.Empty(bus.Paula.Writes);
    }

    private static void ConfigureAllocation(AmigaBus bus, uint request, uint choices, sbyte priority, bool nowait = false)
    {
        bus.WriteLong(request + 0x14, Device);
        bus.WriteWord(request + 0x1C, 32);
        bus.WriteByte(request + 0x1E, nowait ? (byte)0x40 : (byte)0, 0);
        bus.WriteByte(request + 9, unchecked((byte)priority), 0);
        bus.WriteLong(request + 0x2A, choices);
        bus.WriteLong(request + 0x2E, 1);
        bus.WriteWord(request + 0x28, 0);
    }

    private static void InitializeLiveAudioDevice(AmigaBus bus)
    {
        const uint list = ExecBase + 0x15E;
        bus.WriteLong(list, list + 4); bus.WriteLong(list + 4, 0); bus.WriteLong(list + 8, list);
        WriteCString(bus, Name, "audio.device");
        bus.WriteLong(Device + 0x0A, Name);
        bus.WriteLong(Device, list + 4); bus.WriteLong(Device + 4, list);
        bus.WriteLong(list, Device); bus.WriteLong(list + 8, Device);
    }

    private static void WriteCString(AmigaBus bus, uint address, string value)
    { for (var i = 0; i < value.Length; i++) bus.WriteByte(address + (uint)i, (byte)value[i], 0); bus.WriteByte(address + (uint)value.Length, 0, 0); }

    private static bool Invoke(AmigaBus bus, uint address, M68kCpuState state)
        => bus.ReadWord(address) == 0xFF00 && bus.TryInvokeHostGateway(address, bus.ReadLong(address + 2), state);
}
