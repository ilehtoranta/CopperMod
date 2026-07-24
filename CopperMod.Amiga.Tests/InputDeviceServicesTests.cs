using Copper68k;
using CopperMod.Amiga;
using CopperMod.Amiga.CopperStart.Devices.Input;

namespace CopperMod.Amiga.Tests;

public sealed class InputDeviceServicesTests
{
    [Fact]
    public void BeginIoTemporarilyForwardsToTheOriginalRomVectorThenRestoresGateway()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        const uint execBase = 0x3000, input = 0x3500, name = 0x3600, request = 0x3800, handler = 0x3900, handlers = 0x3B00, continuation = 0x00F0_8700;
        bus.WriteLong(execBase + 0x15E, input); bus.WriteLong(execBase + 0x15E + 4, execBase + 0x15E + 4); bus.WriteLong(execBase + 0x15E + 8, input);
        bus.WriteLong(input, execBase + 0x15E + 4); bus.WriteLong(input + 0x0A, name);
        foreach (var (index, character) in "input.device\0".Select((value, index) => (index, value))) bus.WriteByte(name + (uint)index, (byte)character, 0);
        using var service = new InputDeviceServices(bus, _ => { });
        Assert.True(service.TryInstall(execBase));
        Assert.True(bus.HasHostGateway(input - 30));
        Assert.Equal(2, service.GatewayRegistrationCount);

        var state = new M68kCpuState { Cycles = 10, ProgramCounter = input - 24 };
        state.A[1] = request; state.A[7] = 0x4000; bus.WriteLong(state.A[7], 0x2222, 10);
        bus.WriteWord(request + 0x1C, 9); bus.WriteLong(request + 0x28, handler); bus.WriteByte(request + 0x1F, 0, 10);
        Assert.True(bus.TryInvokeHostGateway(input - 30, bus.ReadLong(input - 28), state));
        Assert.Equal(input - 30, state.ProgramCounter);
        Assert.False(bus.HasHostGateway(input - 30));
        Assert.Equal(continuation, bus.ReadLong(state.A[7]));

        state.ProgramCounter = continuation + 6;
        Assert.True(bus.TryInvokeHostGateway(continuation, bus.ReadLong(continuation + 2), state));
        Assert.True(bus.HasHostGateway(input - 30));
        Assert.Equal(2, service.GatewayRegistrationCount);
        // input.device completes handler registration asynchronously.  Model
        // its ReplyMsg completion and the live Exec List that it owns.
        bus.WriteLong(handlers, handler); bus.WriteLong(handlers + 4, 0); bus.WriteLong(handlers + 8, handler);
        bus.WriteLong(handler, handlers + 4); bus.WriteLong(handler + 4, handlers); bus.WriteByte(request + 8, 7, 10);
        service.ProcessPending();
        Assert.Contains(handler, service.KnownNativeHandlers);

        const uint firstEvent = 0x3A00, secondEvent = 0x3A20;
        bus.WriteLong(firstEvent, secondEvent); bus.WriteByte(firstEvent + 4, 1, 10); bus.WriteWord(firstEvent + 6, 0x20, 10); bus.WriteWord(firstEvent + 8, 1, 10);
        bus.WriteLong(secondEvent, 0); bus.WriteByte(secondEvent + 4, 1, 10); bus.WriteWord(secondEvent + 6, 0xA0, 10); bus.WriteWord(secondEvent + 8, 0, 10);
        state.A[1] = request; state.A[7] = 0x4000; bus.WriteLong(state.A[7], 0x2222, 10);
        bus.WriteWord(request + 0x1C, 11); bus.WriteLong(request + 0x28, firstEvent);
        Assert.True(bus.TryInvokeHostGateway(input - 30, bus.ReadLong(input - 28), state));
        Assert.Collection(service.ObservedWriteEvents,
            item => { Assert.Equal(firstEvent, item.Address); Assert.Equal((ushort)0x20, item.Code); Assert.Equal(secondEvent, item.Next); },
            item => { Assert.Equal(secondEvent, item.Address); Assert.Equal((ushort)0xA0, item.Code); Assert.Equal(0u, item.Next); });
    }
}
