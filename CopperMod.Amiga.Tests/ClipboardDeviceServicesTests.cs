using Copper68k;
using CopperMod.Amiga;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CopperStart.Devices.Clipboard;
using CopperMod.Amiga.CopperStart.Exec;

namespace CopperMod.Amiga.Tests;

public sealed class ClipboardDeviceServicesTests
{
    private const uint ExecBase = 0x3000, Allocation = 0x4000, Request = 0x5000, Data = 0x5100, ReadData = 0x5200, WriteRequest = 0x5300;

    [Fact]
    public void CreatesGuestDeviceAndRoundTripsOpaqueIffStream()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeList(bus);
        var replies = new List<uint>();
        using var service = new ClipboardDeviceServices(
            bus,
            new ExecMemoryOperations((size, _) => size == 0x100 ? Allocation : 0x4400, (_, _) => { }, (address, length) => bus.ClearMemory(address, length)),
            replies.Add, (_, _, _) => { }, (_, _, _) => { }, 0xF08930);
        Assert.True(service.TryInstall(ExecBase));
        Assert.Equal(Allocation + 42, service.DeviceBase);
        Assert.Equal(service.DeviceBase, bus.ReadLong(ExecBase + 0x15E));
        Assert.True(bus.HasHostGateway(service.DeviceBase - 30));

        var state = new M68kCpuState { Cycles = 12 };
        state.A[1] = Request; state.D[0] = 0;
        Assert.True(Invoke(bus, service.DeviceBase - 6, state));
        Assert.Equal(service.DeviceBase, bus.ReadLong(Request + 0x14));

        var iff = new byte[] { (byte)'F', (byte)'O', (byte)'R', (byte)'M', 0, 0, 0, 4, (byte)'F', (byte)'T', (byte)'X', (byte)'T' };
        for (var index = 0; index < iff.Length; index++) bus.WriteByte(Data + (uint)index, iff[index], 12);
        PrepareIo(bus, 3, Data, (uint)iff.Length, 0);
        Assert.True(Invoke(bus, service.DeviceBase - 30, state));
        var id = bus.ReadLong(Request + 0x30);
        Assert.NotEqual(0u, id);

        PrepareIo(bus, 4, 0, 0, 0); bus.WriteLong(Request + 0x30, id);
        Assert.True(Invoke(bus, service.DeviceBase - 30, state));
        Assert.Equal(id, bus.ReadLong(Request + 0x30));

        replies.Clear(); PrepareIo(bus, 2, ReadData, 32, 0);
        Assert.True(Invoke(bus, service.DeviceBase - 30, state));
        Assert.Equal((uint)iff.Length, bus.ReadLong(Request + 0x20));
        Assert.Equal(bus.ReadLong(Request + 0x30), id);
        Assert.Equal(iff, Enumerable.Range(0, iff.Length).Select(index => bus.ReadByte(ReadData + (uint)index)).ToArray());
        Assert.Equal(new[] { Request }, replies);
    }

    [Fact]
    public void ClipIdsRejectStaleWritesAndClearCreatesNewEmptyGeneration()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeList(bus);
        using var service = new ClipboardDeviceServices(bus, new ExecMemoryOperations((size, _) => size == 0x100 ? Allocation : 0x4400, (_, _) => { }, (address, length) => bus.ClearMemory(address, length)), _ => { }, (_, _, _) => { }, (_, _, _) => { }, 0xF08930);
        Assert.True(service.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 1 }; state.A[1] = Request; state.D[0] = 0;
        Assert.True(Invoke(bus, service.DeviceBase - 6, state));
        bus.WriteByte(Data, 0xAA, 1); PrepareIo(bus, 3, Data, 1, 0);
        Assert.True(Invoke(bus, service.DeviceBase - 30, state));
        var id = bus.ReadLong(Request + 0x30);
        PrepareIo(bus, 4, 0, 0, 0); bus.WriteLong(Request + 0x30, id);
        Assert.True(Invoke(bus, service.DeviceBase - 30, state));
        PrepareIo(bus, 5, 0, 0, 0);
        Assert.True(Invoke(bus, service.DeviceBase - 30, state));
        PrepareIo(bus, 3, Data, 1, 0); bus.WriteLong(Request + 0x30, id);
        Assert.True(Invoke(bus, service.DeviceBase - 30, state));
        Assert.Equal((byte)1, bus.ReadByte(Request + 0x1F));
        PrepareIo(bus, 2, ReadData, 1, 0);
        Assert.True(Invoke(bus, service.DeviceBase - 30, state));
        Assert.Equal(0u, bus.ReadLong(Request + 0x20));
    }

    [Fact]
    public void FtxtCodecKeepsClassicChrsAndUnicodeUtf8Text()
    {
        var bytes = ClipboardIffText.Encode("Häme 👋");
        Assert.Equal(new byte[] { (byte)'F', (byte)'O', (byte)'R', (byte)'M' }, bytes[..4]);
        Assert.True(ClipboardIffText.TryDecode(bytes, out var text));
        Assert.Equal("Häme 👋", text);
    }

    [Fact]
    public void FtxtCodecConcatenatesClassicChrsChunksAndRejectsTruncatedChunks()
    {
        var ftxt = new byte[]
        {
            (byte)'F',(byte)'O',(byte)'R',(byte)'M', 0,0,0,34, (byte)'F',(byte)'T',(byte)'X',(byte)'T',
            (byte)'C',(byte)'H',(byte)'R',(byte)'S', 0,0,0,1, (byte)'A',0,
            (byte)'J',(byte)'U',(byte)'N',(byte)'K', 0,0,0,2, (byte)'?',(byte)'?',
            (byte)'C',(byte)'H',(byte)'R',(byte)'S', 0,0,0,2, (byte)'B',(byte)'C'
        };
        Assert.True(ClipboardIffText.TryDecode(ftxt, out var text)); Assert.Equal("ABC", text);
        ftxt[16] = 0x7F; ftxt[17] = 0xFF; ftxt[18] = 0xFF; ftxt[19] = 0xFF; // Beyond the declared FORM.
        Assert.False(ClipboardIffText.TryDecode(ftxt, out _));
    }

    [Fact]
    public void IlbmCodecRoundTripsIndexedPixelsWithoutLeakingAHostImageType()
    {
        var source = new ClipboardImage(2, 2,
        [0xFF00_0000, 0xFFFF_0000, 0xFF00_FF00, 0xFF00_00FF]);
        var ilbm = ClipboardIffImage.Encode(source);
        Assert.Equal("FORM", System.Text.Encoding.ASCII.GetString(ilbm[..4]));
        Assert.True(ClipboardIffImage.TryDecode(ilbm, out var decoded));
        Assert.NotNull(decoded); Assert.Equal(2, decoded!.Width); Assert.Equal(2, decoded.Height);
        Assert.Equal(source.Bgra32, decoded.Bgra32);
    }

    [Fact]
    public void IlbmCodecQuantizesImagesWithMoreThan256ColoursToItsOwnPalette()
    {
        var source = new ClipboardImage(257, 1, Enumerable.Range(0, 257)
            .Select(index => 0xFF00_0000u | ((uint)index << 8) | (uint)(index & 0xFF)).ToArray());
        var ilbm = ClipboardIffImage.Encode(source);
        Assert.True(ClipboardIffImage.TryDecode(ilbm, out var decoded));
        Assert.NotNull(decoded);
        Assert.Equal(257, decoded!.Bgra32.Length);
        Assert.Equal(0xFF00_4900u, decoded.Bgra32[0x100]);
    }

    [Fact]
    public void PostDefersReadUntilMatchingWriteAndUpdate()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeList(bus);
        var next = 0x4400u; var replies = new List<uint>(); var posted = new List<(uint Port, uint Message)>();
        using var service = new ClipboardDeviceServices(bus,
            new ExecMemoryOperations((size, _) => size == 0x100 ? Allocation : (next += 0x40) - 0x40, (_, _) => { }, (address, length) => bus.ClearMemory(address, length)),
            replies.Add, (port, message, _) => posted.Add((port, message)), (_, _, _) => { }, 0xF08930);
        Assert.True(service.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 8 }; state.A[1] = Request; state.D[0] = 0;
        Assert.True(Invoke(bus, service.DeviceBase - 6, state));

        const uint port = 0x6000;
        bus.ClearMemory(port, 0x30); PrepareIo(bus, 9, port, 0, 0);
        Assert.True(Invoke(bus, service.DeviceBase - 30, state));
        var id = bus.ReadLong(Request + 0x30);
        Assert.NotEqual(0u, id);
        replies.Clear(); PrepareIo(bus, 2, ReadData, 4, 0);
        Assert.True(Invoke(bus, service.DeviceBase - 30, state));
        Assert.Empty(replies); Assert.Single(posted);
        Assert.Equal((ushort)0, bus.ReadWord(posted[0].Message + 0x14));
        Assert.Equal(id, bus.ReadLong(posted[0].Message + 0x18));

        bus.WriteLong(WriteRequest + 0x14, service.DeviceBase); bus.WriteLong(WriteRequest + 0x18, bus.ReadLong(Request + 0x18));
        bus.WriteByte(Data, 0xA5, 8); PrepareIo(bus, WriteRequest, 3, Data, 1, 0); bus.WriteLong(WriteRequest + 0x30, id); state.A[1] = WriteRequest;
        Assert.True(Invoke(bus, service.DeviceBase - 30, state));
        PrepareIo(bus, WriteRequest, 4, 0, 0, 0); bus.WriteLong(WriteRequest + 0x30, id);
        Assert.True(Invoke(bus, service.DeviceBase - 30, state));
        Assert.Contains(Request, replies);
        Assert.Equal((byte)0xA5, bus.ReadByte(ReadData));
    }

    [Fact]
    public void ChangeHookIsEnteredOnlyFromBoundaryProcessing()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeList(bus);
        uint next = 0x4400, entry = 0, continuation = 0, hookMessage = 0;
        using var service = new ClipboardDeviceServices(bus,
            new ExecMemoryOperations((size, _) => size == 0x100 ? Allocation : (next += 0x40) - 0x40, (_, _) => { }, (address, length) => bus.ClearMemory(address, length)),
            _ => { }, (_, _, _) => { }, (state, target, resume) => { entry = target; continuation = resume; hookMessage = state.A[1]; }, 0xF08930);
        Assert.True(service.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 4 }; state.A[1] = Request; state.D[0] = 0;
        Assert.True(Invoke(bus, service.DeviceBase - 6, state));
        const uint hook = 0x7000, hookEntry = 0x7100;
        bus.WriteLong(hook + 8, hookEntry); PrepareIo(bus, 12, hook, 1, 0);
        Assert.True(Invoke(bus, service.DeviceBase - 30, state));
        PrepareIo(bus, 5, 0, 0, 0);
        Assert.True(Invoke(bus, service.DeviceBase - 30, state));
        Assert.Equal(0u, entry);
        service.ProcessPending(state);
        Assert.Equal(hookEntry, entry); Assert.Equal(0xF08930u, continuation);
        Assert.Equal(4u, bus.ReadLong(hookMessage + 4)); // CMD_UPDATE
        service.ContinueHook(state);
    }

    [Fact]
    public void ResetUnlinksHostDeviceAndRemovesDirectGateways()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeList(bus);
        var service = new ClipboardDeviceServices(bus,
            new ExecMemoryOperations((size, _) => size == 0x100 ? Allocation : 0x4400, (_, _) => { }, (address, length) => bus.ClearMemory(address, length)),
            _ => { }, (_, _, _) => { }, (_, _, _) => { }, 0xF08930);
        Assert.True(service.TryInstall(ExecBase));
        var open = service.DeviceBase - 6;
        service.Reset();
        Assert.Equal(ExecBase + 0x162, bus.ReadLong(ExecBase + 0x15E));
        Assert.False(bus.HasHostGateway(open));
        service.Dispose();
    }

    [Fact]
    public void HostTextIsAppliedOnlyAtBoundaryAndBecomesFtxt()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeList(bus);
        using var service = new ClipboardDeviceServices(bus,
            new ExecMemoryOperations((size, _) => size == 0x100 ? Allocation : 0x4400, (_, _) => { }, (address, length) => bus.ClearMemory(address, length)),
            _ => { }, (_, _, _) => { }, (_, _, _) => { }, 0xF08930);
        Assert.True(service.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 7 }; state.A[1] = Request; state.D[0] = 0;
        Assert.True(Invoke(bus, service.DeviceBase - 6, state));
        service.QueuePrimaryTextFromHost("clipboard");
        PrepareIo(bus, 2, ReadData, 64, 0);
        Assert.True(Invoke(bus, service.DeviceBase - 30, state));
        Assert.Equal(0u, bus.ReadLong(Request + 0x20));
        service.ProcessPending(state);
        PrepareIo(bus, 2, ReadData, 64, 0);
        Assert.True(Invoke(bus, service.DeviceBase - 30, state));
        var count = bus.ReadLong(Request + 0x20);
        Assert.True(count >= 12);
        Assert.True(ClipboardIffText.TryDecode(Enumerable.Range(0, (int)count).Select(index => bus.ReadByte(ReadData + (uint)index)).ToArray(), out var text));
        Assert.Equal("clipboard", text);
    }

    [Fact]
    public void HostAndGuestImagesCrossTheBoundaryAsIlbm()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeList(bus);
        using var service = new ClipboardDeviceServices(bus,
            new ExecMemoryOperations((size, _) => size == 0x100 ? Allocation : 0x4400, (_, _) => { }, (address, length) => bus.ClearMemory(address, length)),
            _ => { }, (_, _, _) => { }, (_, _, _) => { }, 0xF08930);
        Assert.True(service.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 7 }; state.A[1] = Request; state.D[0] = 0;
        Assert.True(Invoke(bus, service.DeviceBase - 6, state));
        var image = new ClipboardImage(2, 1, [0xFFFF_0000, 0xFF00_00FF]);

        service.QueuePrimaryImageFromHost(image);
        service.ProcessPending(state);
        PrepareIo(bus, 2, ReadData, 128, 0);
        Assert.True(Invoke(bus, service.DeviceBase - 30, state));
        var hostBytes = Enumerable.Range(0, (int)bus.ReadLong(Request + 0x20)).Select(index => bus.ReadByte(ReadData + (uint)index)).ToArray();
        Assert.True(ClipboardIffImage.TryDecode(hostBytes, out var decodedFromHost));
        Assert.Equal(image.Bgra32, decodedFromHost!.Bgra32);

        var guestImage = new ClipboardImage(1, 2, [0xFF00_FF00, 0xFFFF_FFFF]);
        var guestIlbm = ClipboardIffImage.Encode(guestImage);
        for (var index = 0; index < guestIlbm.Length; index++) bus.WriteByte(Data + (uint)index, guestIlbm[index], state.Cycles);
        PrepareIo(bus, 3, Data, (uint)guestIlbm.Length, 0);
        Assert.True(Invoke(bus, service.DeviceBase - 30, state));
        var id = bus.ReadLong(Request + 0x30);
        PrepareIo(bus, 4, 0, 0, 0); bus.WriteLong(Request + 0x30, id);
        Assert.True(Invoke(bus, service.DeviceBase - 30, state));
        Assert.True(service.TryTakePrimaryImageForHost(out var decodedFromGuest));
        Assert.Equal(guestImage.Bgra32, decodedFromGuest!.Bgra32);
    }

    [Fact]
    public void StandardDevicePrefixStopsStartsFlushesAndResetsClipboardRequests()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeList(bus);
        var replies = new List<uint>();
        using var service = new ClipboardDeviceServices(bus,
            new ExecMemoryOperations((size, _) => size == 0x100 ? Allocation : 0x4400, (_, _) => { }, (address, length) => bus.ClearMemory(address, length)),
            replies.Add, (_, _, _) => { }, (_, _, _) => { }, 0xF08930);
        Assert.True(service.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 9 }; state.A[1] = Request; state.D[0] = 0;
        Assert.True(Invoke(bus, service.DeviceBase - 6, state));
        var unit = bus.ReadLong(Request + 0x18);

        PrepareIo(bus, 6, 0, 0, 0); // CMD_STOP
        Assert.True(Invoke(bus, service.DeviceBase - 30, state));
        replies.Clear();
        bus.WriteLong(WriteRequest + 0x14, service.DeviceBase); bus.WriteLong(WriteRequest + 0x18, unit);
        bus.WriteByte(Data, 0xAA, state.Cycles); PrepareIo(bus, WriteRequest, 3, Data, 1, 0); state.A[1] = WriteRequest;
        Assert.True(Invoke(bus, service.DeviceBase - 30, state));
        Assert.Empty(replies); // The write is queued while stopped.

        const uint startRequest = 0x5400;
        bus.WriteLong(startRequest + 0x14, service.DeviceBase); bus.WriteLong(startRequest + 0x18, unit);
        PrepareIo(bus, startRequest, 7, 0, 0, 0); state.A[1] = startRequest;
        Assert.True(Invoke(bus, service.DeviceBase - 30, state));
        Assert.Contains(WriteRequest, replies); Assert.Contains(startRequest, replies);

        // A posted clip turns a read into an active request. CMD_FLUSH must
        // cancel it exactly once without invalidating the persisted clip.
        state.A[1] = Request; const uint port = 0x6000; bus.ClearMemory(port, 0x30);
        PrepareIo(bus, 9, port, 0, 0); Assert.True(Invoke(bus, service.DeviceBase - 30, state));
        replies.Clear(); PrepareIo(bus, 2, ReadData, 8, 0); Assert.True(Invoke(bus, service.DeviceBase - 30, state));
        Assert.Empty(replies);
        PrepareIo(bus, startRequest, 8, 0, 0, 0); state.A[1] = startRequest;
        Assert.True(Invoke(bus, service.DeviceBase - 30, state));
        Assert.Contains(Request, replies); Assert.Equal(0xFE, bus.ReadByte(Request + 0x1F));

        PrepareIo(bus, startRequest, 1, 0, 0, 0); // CMD_RESET restores command processing.
        Assert.True(Invoke(bus, service.DeviceBase - 30, state));
        Assert.Equal((byte)0, bus.ReadByte(startRequest + 0x1F));
    }

    [Fact]
    public void ClipboardUnitsKeepIndependentOpaqueStreams()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeList(bus);
        var next = 0x4400u;
        using var service = new ClipboardDeviceServices(bus,
            new ExecMemoryOperations((size, _) => size == 0x100 ? Allocation : (next += 0x40) - 0x40, (_, _) => { }, (address, length) => bus.ClearMemory(address, length)),
            _ => { }, (_, _, _) => { }, (_, _, _) => { }, 0xF08930);
        Assert.True(service.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0;
        Assert.True(Invoke(bus, service.DeviceBase - 6, state));
        const uint second = 0x5500;
        state.A[1] = second; state.D[0] = 1;
        Assert.True(Invoke(bus, service.DeviceBase - 6, state));
        Assert.NotEqual(bus.ReadLong(Request + 0x18), bus.ReadLong(second + 0x18));

        bus.WriteByte(Data, 0xA1, state.Cycles);
        PrepareIo(bus, second, 3, Data, 1, 0); state.A[1] = second;
        Assert.True(Invoke(bus, service.DeviceBase - 30, state));
        var id = bus.ReadLong(second + 0x30);
        PrepareIo(bus, second, 4, 0, 0, 0); bus.WriteLong(second + 0x30, id);
        Assert.True(Invoke(bus, service.DeviceBase - 30, state));

        state.A[1] = Request; PrepareIo(bus, 2, ReadData, 1, 0);
        Assert.True(Invoke(bus, service.DeviceBase - 30, state)); Assert.Equal(0u, bus.ReadLong(Request + 0x20));
        state.A[1] = second; PrepareIo(bus, second, 2, ReadData, 1, 0);
        Assert.True(Invoke(bus, service.DeviceBase - 30, state)); Assert.Equal(1u, bus.ReadLong(second + 0x20)); Assert.Equal((byte)0xA1, bus.ReadByte(ReadData));
    }

    private static void InitializeList(AmigaBus bus)
    {
        bus.WriteLong(ExecBase + 0x15E, ExecBase + 0x162); bus.WriteLong(ExecBase + 0x162, 0); bus.WriteLong(ExecBase + 0x166, ExecBase + 0x15E);
    }
    private static void PrepareIo(AmigaBus bus, ushort command, uint data, uint length, uint offset)
        => PrepareIo(bus, Request, command, data, length, offset);
    private static void PrepareIo(AmigaBus bus, uint request, ushort command, uint data, uint length, uint offset)
    {
        bus.WriteWord(request + 0x1C, command); bus.WriteByte(request + 0x1E, 0, 1); bus.WriteLong(request + 0x28, data); bus.WriteLong(request + 0x24, length); bus.WriteLong(request + 0x2C, offset); bus.WriteLong(request + 0x30, 0);
    }
    private static bool Invoke(AmigaBus bus, uint address, M68kCpuState state)
        => bus.ReadWord(address) == 0xFF00 && bus.TryInvokeHostGateway(address, bus.ReadLong(address + 2), state);
}
