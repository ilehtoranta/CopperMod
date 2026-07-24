using Copper68k;
using CopperMod.Amiga;
using CopperMod.Amiga.CopperStart.Devices.Timer;

namespace CopperMod.Amiga.Tests;

public sealed class TimerDeviceServicesTests
{
    private const int ExecDeviceListOffset = 0x15E;
    private const int NodeNameOffset = 0x0A;
    private const uint ExecBase = 0x3000;
    private const uint Device = 0x3500;
    private const uint Name = 0x3600;
    private const uint Request = 0x3700;
    private const uint EClock = 0x3800;

    [Fact]
    public void MicroHzRequestUsesMachineCyclesRepliesOnceAndDoesNotTouchCias()
    {
        var machine = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false));
        var bus = machine.Bus;
        var replies = new List<uint>();
        InitializeLiveTimerDevice(bus);
        var ciaA = bus.ReadByte(0x00BFE001);
        var ciaB = bus.ReadByte(0x00BFD000);
        using var service = new TimerDeviceServices(bus, replies.Add, _ => { });
        Assert.True(service.TryInstall(ExecBase));
        Assert.True(bus.HasHostGateway(Device - 30));

        Open(bus, service, unit: 0);
        bus.WriteWord(Request + 0x1C, 9); // TR_ADDREQUEST
        bus.WriteLong(Request + 0x20, 0);
        bus.WriteLong(Request + 0x24, 10); // ten microseconds
        var begin = new M68kCpuState { Cycles = 100 };
        begin.A[1] = Request;
        Assert.True(Invoke(bus, Device - 30, begin));

        var expectedDeadline = 100L + 71L; // ceil(10 us * 7,093,790 Hz)
        Assert.Equal(expectedDeadline, service.GetNextDeadline(100, 1_000));
        Assert.Empty(replies);
        begin.Cycles = expectedDeadline - 1;
        service.ProcessPending(begin);
        Assert.Empty(replies);
        begin.Cycles = expectedDeadline;
        service.ProcessPending(begin);
        Assert.Equal(new[] { Request }, replies);
        Assert.Equal(0, bus.ReadByte(Request + 0x1F));
        Assert.Equal(ciaA, bus.ReadByte(0x00BFE001));
        Assert.Equal(ciaB, bus.ReadByte(0x00BFD000));
    }

    [Fact]
    public void VBlankAndEClockFollowTheFixedPalOrNtscCadenceWithoutCiaTimers()
    {
        VerifyOptions(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot));
        VerifyOptions(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithChipset(AmigaChipset.OcsNtsc));
        VerifyOptions(MachineOptions.ForProfile(MachineProfile.A500PlusEcsPal));
        VerifyOptions(MachineOptions.ForProfile(MachineProfile.A500PlusEcsNtsc));
    }

    [Fact]
    public void AbortRemovesPendingRequestAndRepliesOnlyOnce()
    {
        var machine = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false));
        var bus = machine.Bus;
        var replies = new List<uint>();
        InitializeLiveTimerDevice(bus);
        using var service = new TimerDeviceServices(bus, replies.Add, _ => { });
        Assert.True(service.TryInstall(ExecBase));
        Open(bus, service, unit: 0);
        bus.WriteWord(Request + 0x1C, 9);
        bus.WriteLong(Request + 0x20, 1);
        bus.WriteLong(Request + 0x24, 0);
        var state = new M68kCpuState { Cycles = 0 };
        state.A[1] = Request;
        Assert.True(Invoke(bus, Device - 30, state));
        Assert.True(service.GetNextDeadline(0, long.MaxValue) > 0);
        Assert.True(Invoke(bus, Device - 36, state));
        Assert.Equal(0u, state.D[0]);
        Assert.Equal(0xFE, bus.ReadByte(Request + 0x1F));
        Assert.Equal(new[] { Request }, replies);
        state.Cycles = long.MaxValue / 2;
        service.ProcessPending(state);
        Assert.Equal(new[] { Request }, replies);
    }

    [Fact]
    public void VBlankRequestIgnoresEcsProgrammableVBlankBoundary()
    {
        var machine = new Machine(MachineOptions.ForProfile(MachineProfile.A500PlusEcsPal).WithLiveAgnusDma(false));
        var bus = machine.Bus;
        InitializeLiveTimerDevice(bus);
        using var service = new TimerDeviceServices(bus, _ => { }, _ => { });
        Assert.True(service.TryInstall(ExecBase));
        Open(bus, service, unit: 1);
        var cycle = 0L;
        bus.WriteWord(0x00DFF1CC, 100, ref cycle, AmigaBusAccessKind.CpuDataWrite); // VBSTRT
        bus.WriteWord(0x00DFF1DC, 0x1000, ref cycle, AmigaBusAccessKind.CpuDataWrite); // BEAMCON0: VARVBEN
        bus.WriteWord(Request + 0x1C, 9);
        bus.WriteLong(Request + 0x20, 0);
        bus.WriteLong(Request + 0x24, 0);
        var state = new M68kCpuState { Cycles = cycle };
        state.A[1] = Request;
        Assert.True(Invoke(bus, Device - 30, state));
        var expected = (bus.RasterTiming.CpuClockHz + 49) / 50;
        Assert.Equal(expected, service.GetNextDeadline(state.Cycles, long.MaxValue));
    }

    [Fact]
    public void SystemTimeCommandsAreDeterministicAndAbsoluteRequestsUseTheGuestEpoch()
    {
        var machine = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false));
        var bus = machine.Bus;
        InitializeLiveTimerDevice(bus);
        using var service = new TimerDeviceServices(bus, _ => { }, _ => { });
        Assert.True(service.TryInstall(ExecBase));
        Open(bus, service, unit: 3);
        var state = new M68kCpuState { Cycles = bus.RasterTiming.CpuClockHz };
        state.A[1] = Request;

        bus.WriteWord(Request + 0x1C, 10); // TR_GETSYSTIME
        bus.WriteByte(Request + 0x1E, 1, 0);
        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal(1u, bus.ReadLong(Request + 0x20));
        Assert.Equal(0u, bus.ReadLong(Request + 0x24));

        bus.WriteWord(Request + 0x1C, 11); // TR_SETSYSTIME
        bus.WriteLong(Request + 0x20, 4);
        bus.WriteLong(Request + 0x24, 500);
        Assert.True(Invoke(bus, Device - 30, state));

        bus.WriteWord(Request + 0x1C, 10);
        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal(4u, bus.ReadLong(Request + 0x20));
        Assert.Equal(500u, bus.ReadLong(Request + 0x24));

        // UNIT_WAITUNTIL is absolute system time, not a host-clock deadline.
        bus.WriteWord(Request + 0x1C, 9);
        bus.WriteLong(Request + 0x20, 4);
        bus.WriteLong(Request + 0x24, 510);
        Assert.True(Invoke(bus, Device - 30, state));
        var deadline = state.Cycles + 71; // ten microseconds at PAL CPU frequency
        Assert.Equal(deadline, service.GetNextDeadline(state.Cycles, long.MaxValue));
        state.Cycles = deadline;
        service.ProcessPending(state);
        Assert.Equal(0, bus.ReadByte(Request + 0x1F));
    }

    private static void VerifyOptions(MachineOptions options)
    {
        var machine = new Machine(options.WithLiveAgnusDma(false));
        var bus = machine.Bus;
        InitializeLiveTimerDevice(bus);
        using var service = new TimerDeviceServices(bus, _ => { }, _ => { });
        Assert.True(service.TryInstall(ExecBase));
        Open(bus, service, unit: 1);
        bus.WriteWord(Request + 0x1C, 9);
        bus.WriteLong(Request + 0x20, 0);
        bus.WriteLong(Request + 0x24, 0);
        var state = new M68kCpuState();
        state.A[1] = Request;
        Assert.True(Invoke(bus, Device - 30, state));
        var vblankHz = bus.RasterTiming.IsCanonicalNtsc ? 60L : 50L;
        var expectedVBlank = (bus.RasterTiming.CpuClockHz + (vblankHz - 1)) / vblankHz;
        Assert.Equal(expectedVBlank, service.GetNextDeadline(0, long.MaxValue));

        state.A[0] = EClock;
        state.Cycles = bus.RasterTiming.CpuClockHz;
        Assert.True(Invoke(bus, Device - 42, state));
        Assert.Equal((uint)(bus.RasterTiming.CpuClockHz / 10), state.D[0]);
        Assert.Equal((uint)(bus.RasterTiming.CpuClockHz / 10), bus.ReadLong(EClock + 4));
    }

    private static void Open(AmigaBus bus, TimerDeviceServices service, int unit)
    {
        var state = new M68kCpuState();
        state.A[1] = Request;
        state.D[0] = unchecked((uint)unit);
        Assert.True(Invoke(bus, Device - 6, state));
        Assert.Equal(0u, state.D[0]);
        Assert.Equal(Device, bus.ReadLong(Request + 0x14));
        bus.WriteLong(Request + 0x0E, 0x3900);
    }

    private static void InitializeLiveTimerDevice(AmigaBus bus)
    {
        InitializeList(bus, ExecBase + ExecDeviceListOffset);
        WriteCString(bus, Name, "timer.device");
        bus.WriteLong(Device + NodeNameOffset, Name);
        bus.WriteLong(Device, ExecBase + ExecDeviceListOffset + 4);
        bus.WriteLong(Device + 4, ExecBase + ExecDeviceListOffset);
        bus.WriteLong(ExecBase + ExecDeviceListOffset, Device);
        bus.WriteLong(ExecBase + ExecDeviceListOffset + 8, Device);
    }

    private static void InitializeList(AmigaBus bus, uint list)
    {
        bus.WriteLong(list, list + 4);
        bus.WriteLong(list + 4, 0);
        bus.WriteLong(list + 8, list);
    }

    private static void WriteCString(AmigaBus bus, uint address, string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            bus.WriteByte(address + (uint)index, (byte)value[index], 0);
        }

        bus.WriteByte(address + (uint)value.Length, 0, 0);
    }

    private static bool Invoke(AmigaBus bus, uint address, M68kCpuState state)
        => bus.ReadWord(address) == 0xFF00 && bus.TryInvokeHostGateway(address, bus.ReadLong(address + 2), state);
}
