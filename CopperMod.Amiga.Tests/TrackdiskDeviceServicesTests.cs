using CopperMod.Amiga;
using CopperMod.Amiga.CopperStart.Devices.Trackdisk;

namespace CopperMod.Amiga.Tests;

/// <summary>Focused device-level coverage; boot integration remains in AmigaBootMemoryTests.</summary>
public sealed class TrackdiskDeviceServicesTests
{
    private const int ExecDeviceListOffset = 0x15E;
    private const int NodeNameOffset = 0x0A;

    [Fact]
    public void InstallsOnlyLiveTrackdiskVectorsAndUsesLogicalMediaWriter()
    {
        var machine = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false));
        var bus = machine.Bus;
        const uint execBase = 0x3000, device = 0x3500, name = 0x3600, request = 0x3700, source = 0x3800;
        var data = new byte[1024];
        var dirty = false;
        InitializeList(bus, execBase + ExecDeviceListOffset);
        WriteCString(bus, name, "trackdisk.device");
        bus.WriteLong(device + NodeNameOffset, name);
        bus.WriteLong(device, execBase + ExecDeviceListOffset + 4);
        bus.WriteLong(device + 4, execBase + ExecDeviceListOffset);
        bus.WriteLong(execBase + ExecDeviceListOffset, device);
        bus.WriteLong(execBase + ExecDeviceListOffset + 8, device);

        using var service = new TrackdiskDeviceServices(
            bus, unit => unit == 0 ? data : null,
            (unit, offset, bytes) => { if (unit != 0) return false; bytes.CopyTo(data.AsSpan(offset)); dirty = true; return true; },
            unit => null, (unit, track) => false, unit => 0, _ => { }, _ => false, _ => false,
            (_, _, _) => { }, _ => { }, _ => { });
        Assert.True(service.TryInstall(execBase));
        Assert.True(bus.HasHostGateway(device - 30));

        var state = new M68kCpuState();
        state.A[1] = request;
        bus.WriteLong(request + 0x14, device);
        bus.WriteLong(request + 0x18, 0);
        bus.WriteWord(request + 0x1C, 3); // CMD_WRITE
        bus.WriteByte(request + 0x1E, 1, 0);
        bus.WriteLong(request + 0x24, 4);
        bus.WriteLong(request + 0x28, source);
        bus.WriteLong(request + 0x2C, 12);
        bus.WriteLong(source, 0x12345678);
        Assert.True(Invoke(bus, device - 30, state));
        Assert.True(dirty);
        Assert.Equal(new byte[] { 0x12, 0x34, 0x56, 0x78 }, data[12..16]);
    }

    [Fact]
    public void LogicalImageWriterMarksMediaDirtyAndInvalidatesTrackCache()
    {
        var disk = AmigaDiskImage.FromAdfBytes(new byte[AmigaDiskImage.StandardAdfSize]);
        var cachedTrack = disk.ReadEncodedTrack(0, 0).EncodedData.ToArray();
        Assert.True(disk.TryWriteBytes(510, new byte[] { 0x12, 0x34, 0x56, 0x78 }));
        Assert.True(disk.IsDirty);
        Assert.NotEqual(cachedTrack, disk.ReadEncodedTrack(0, 0).EncodedData.ToArray());
    }

    private static void InitializeList(AmigaBus bus, uint list)
    { bus.WriteLong(list, list + 4); bus.WriteLong(list + 4, 0); bus.WriteLong(list + 8, list); }
    private static void WriteCString(AmigaBus bus, uint address, string value)
    { for (var i = 0; i < value.Length; i++) bus.WriteByte(address + (uint)i, (byte)value[i], 0); bus.WriteByte(address + (uint)value.Length, 0, 0); }
    private static bool Invoke(AmigaBus bus, uint address, M68kCpuState state)
        => bus.ReadWord(address) == 0xFF00 && bus.TryInvokeHostGateway(address, bus.ReadLong(address + 2), state);
}
