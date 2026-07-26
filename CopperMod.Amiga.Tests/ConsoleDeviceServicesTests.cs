using Copper68k;
using CopperMod.Amiga;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CopperStart.Devices.Clipboard;
using CopperMod.Amiga.CopperStart.Devices.Console;
using CopperMod.Amiga.CopperStart.Devices.Input;
using CopperMod.Amiga.CopperStart.Dos;
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
        Assert.Equal(Data + 2, bus.ReadLong(Request + 0x28)); Assert.Equal(0u, bus.ReadLong(Request + 0x24));

        replies.Clear(); bus.WriteWord(Request + 0x1C, 2); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, 8); bus.WriteByte(Request + 0x1E, 0, 10);
        Assert.True(Invoke(bus, Device - 30, state)); Assert.Empty(replies);
        bus.WriteByte(Event + 4, 1, 10); bus.WriteWord(Event + 6, 0x0020); state.A[0] = Event;
        Assert.True(Invoke(bus, Device - 42, state));
        console.ProcessPending(state);
        Assert.Equal((byte)'a', bus.ReadByte(Data)); Assert.Equal(1u, bus.ReadLong(Request + 0x20)); Assert.Equal(new[] { Request }, replies);
    }

    [Fact]
    public void ConsoleStandardCommandsUseNormalIoErrorsAndResetDoesNotUninstallTheDevice()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, _, _) => { });
        Assert.True(console.TryInstall(ExecBase));

        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0;
        bus.WriteLong(Request + 0x28, Window); Assert.True(Invoke(bus, Device - 6, state));
        bus.WriteWord(Request + 0x1C, 1); Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal((byte)0, bus.ReadByte(Request + 0x1F)); Assert.True(console.IsInstalled);

        bus.WriteWord(Request + 0x1C, 0x7777); Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal((byte)0xFD, bus.ReadByte(Request + 0x1F));
        Assert.True(Invoke(bus, Device - 12, state)); Assert.Equal((ushort)0, bus.ReadWord(Device + 0x20));
    }

    [Fact]
    public void ConsoleStopDefersWritesUntilStart()
    {
        const uint startRequest = 0x3D00;
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        var replies = new List<uint>(); var draws = new List<string>();
        using var console = new ConsoleDeviceServices(bus, memory, input, replies.Add, (_, _, text, length) => draws.Add(string.Concat(Enumerable.Range(0, (int)length).Select(index => (char)bus.ReadByte(text + (uint)index)))));
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0; bus.WriteLong(Request + 0x28, Window);
        Assert.True(Invoke(bus, Device - 6, state));

        bus.WriteWord(Request + 0x1C, 6); Assert.True(Invoke(bus, Device - 30, state));
        replies.Clear(); bus.WriteByte(Data, (byte)'X', 10); bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, 1); bus.WriteByte(Request + 0x1E, 0, 10);
        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Empty(draws); Assert.Empty(replies); Assert.Equal((byte)0, (byte)(bus.ReadByte(Request + 0x1E) & 1));

        bus.WriteLong(startRequest + 0x14, Device); bus.WriteLong(startRequest + 0x18, bus.ReadLong(Request + 0x18)); bus.WriteWord(startRequest + 0x1C, 7); bus.WriteByte(startRequest + 0x1E, 0, 10);
        state.A[1] = startRequest; Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal(new[] { "X" }, draws); Assert.Contains(Request, replies); Assert.Contains(startRequest, replies);
    }

    [Fact]
    public void ConsoleDsrReplyFlowsThroughTheOrdinaryReadQueue()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, _, _) => { });
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0; bus.WriteLong(Request + 0x28, Window);
        Assert.True(Invoke(bus, Device - 6, state));

        var query = new byte[] { 0x1B, (byte)'[', (byte)'5', (byte)'n' };
        for (var index = 0; index < query.Length; index++) bus.WriteByte(Data + (uint)index, query[index], 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, (uint)query.Length);
        Assert.True(Invoke(bus, Device - 30, state));

        bus.WriteWord(Request + 0x1C, 2); bus.WriteLong(Request + 0x28, Data + 16); bus.WriteLong(Request + 0x24, 8);
        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal((byte)0x9B, bus.ReadByte(Data + 16)); Assert.Equal((byte)'0', bus.ReadByte(Data + 17)); Assert.Equal((byte)'n', bus.ReadByte(Data + 18));
    }

    [Fact]
    public void ConsoleWriteWithMinusOneLengthStopsAtTheFirstNull()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        var draws = new List<string>();
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, text, length) => draws.Add(string.Concat(Enumerable.Range(0, (int)length).Select(index => (char)bus.ReadByte(text + (uint)index)))));
        Assert.True(console.TryInstall(ExecBase));

        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0;
        bus.WriteLong(Request + 0x28, Window); Assert.True(Invoke(bus, Device - 6, state));
        foreach (var (index, value) in new byte[] { (byte)'O', (byte)'K', 0, (byte)'!', (byte)'!' }.Select((value, index) => (index, value))) bus.WriteByte(Data + (uint)index, value, 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, uint.MaxValue);

        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal(new[] { "OK" }, draws);
        Assert.Equal(2u, bus.ReadLong(Request + 0x20));
    }

    [Fact]
    public void ConsoleShiftOutSetsTheHighBitUntilShiftIn()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        var draws = new List<byte[]>();
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, text, length) => draws.Add(Enumerable.Range(0, (int)length).Select(index => bus.ReadByte(text + (uint)index)).ToArray()));
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0; bus.WriteLong(Request + 0x28, Window);
        Assert.True(Invoke(bus, Device - 6, state));
        var output = new byte[] { 0x0F, (byte)'A', 0x0E, (byte)'B' };
        for (var index = 0; index < output.Length; index++) bus.WriteByte(Data + (uint)index, output[index], 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, (uint)output.Length);

        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Single(draws); Assert.Equal(new byte[] { 0xC1, (byte)'B' }, draws[0]);
    }

    [Fact]
    public void ConsoleC1VerticalTabMovesUpAndHorizontalTabSetCreatesAPerUnitStop()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, _, _) => { });
        Assert.True(console.TryInstall(ExecBase));

        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0;
        bus.WriteLong(Request + 0x28, Window); Assert.True(Invoke(bus, Device - 6, state));
        var output = System.Text.Encoding.ASCII.GetBytes("\x1b[4B\x0b\x1b[6n");
        for (var index = 0; index < output.Length; index++) bus.WriteByte(Data + (uint)index, output[index], 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, (uint)output.Length);
        Assert.True(Invoke(bus, Device - 30, state));

        bus.WriteWord(Request + 0x1C, 2); bus.WriteLong(Request + 0x28, Data + 32); bus.WriteLong(Request + 0x24, 16);
        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal("\u009B4;1R", System.Text.Encoding.Latin1.GetString(Enumerable.Range(0, 5).Select(index => bus.ReadByte(Data + 32u + (uint)index)).ToArray()));

        output = new byte[] { 0x1B, (byte)'[', (byte)'4', (byte)'G', 0x88, 0x1B, (byte)'[', (byte)'H', (byte)'\t', 0x1B, (byte)'[', (byte)'6', (byte)'n' };
        for (var index = 0; index < output.Length; index++) bus.WriteByte(Data + (uint)index, output[index], 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, (uint)output.Length);
        Assert.True(Invoke(bus, Device - 30, state));
        bus.WriteWord(Request + 0x1C, 2); bus.WriteLong(Request + 0x28, Data + 32); bus.WriteLong(Request + 0x24, 16);
        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal("\u009B1;4R", System.Text.Encoding.Latin1.GetString(Enumerable.Range(0, 5).Select(index => bus.ReadByte(Data + 32u + (uint)index)).ToArray()));
    }

    [Fact]
    public void ConsoleCanAndFreshEscapeDiscardAnUnfinishedCsiSequence()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, _, _) => { });
        Assert.True(console.TryInstall(ExecBase));

        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0;
        bus.WriteLong(Request + 0x28, Window); Assert.True(Invoke(bus, Device - 6, state));
        // The incomplete cursor movement is cancelled.  The later CPR is
        // therefore interpreted independently and reports the origin.
        var output = new byte[] { 0x1B, (byte)'[', (byte)'9', 0x18, 0x1B, (byte)'[', (byte)'6', (byte)'n' };
        for (var index = 0; index < output.Length; index++) bus.WriteByte(Data + (uint)index, output[index], 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, (uint)output.Length);
        Assert.True(Invoke(bus, Device - 30, state));
        bus.WriteWord(Request + 0x1C, 2); bus.WriteLong(Request + 0x28, Data + 32); bus.WriteLong(Request + 0x24, 16);
        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal("\u009B1;1R", System.Text.Encoding.Latin1.GetString(Enumerable.Range(0, 5).Select(index => bus.ReadByte(Data + 32u + (uint)index)).ToArray()));

        // A new ESC likewise replaces a malformed CSI, rather than becoming
        // one of its parameters.
        output = new byte[] { 0x1B, (byte)'[', (byte)'9', 0x1B, (byte)'[', (byte)'5', (byte)'n' };
        for (var index = 0; index < output.Length; index++) bus.WriteByte(Data + (uint)index, output[index], 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, (uint)output.Length);
        Assert.True(Invoke(bus, Device - 30, state));
        bus.WriteWord(Request + 0x1C, 2); bus.WriteLong(Request + 0x28, Data + 32); bus.WriteLong(Request + 0x24, 16);
        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal("\u009B0n", System.Text.Encoding.Latin1.GetString(Enumerable.Range(0, 3).Select(index => bus.ReadByte(Data + 32u + (uint)index)).ToArray()));
    }

    [Fact]
    public void ConsoleDoesNotRenderUnknownC1ControlsAsText()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, _, _) => { });
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0; bus.WriteLong(Request + 0x28, Window);
        Assert.True(Invoke(bus, Device - 6, state));

        var output = new byte[] { 0x80, 0x9B, (byte)'6', (byte)'n' };
        for (var index = 0; index < output.Length; index++) bus.WriteByte(Data + (uint)index, output[index], 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, (uint)output.Length);
        Assert.True(Invoke(bus, Device - 30, state));
        bus.WriteWord(Request + 0x1C, 2); bus.WriteLong(Request + 0x28, Data + 16); bus.WriteLong(Request + 0x24, 16);
        Assert.True(Invoke(bus, Device - 30, state));

        Assert.Equal("\u009B1;1R", System.Text.Encoding.Latin1.GetString(Enumerable.Range(0, 5).Select(index => bus.ReadByte(Data + 16u + (uint)index)).ToArray()));
    }

    [Fact]
    public void ConsoleTreatsDelAsTheDocumentedVisibleG0Character()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        var draws = new List<byte[]>();
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, text, length) => draws.Add(Enumerable.Range(0, (int)length).Select(index => bus.ReadByte(text + (uint)index)).ToArray()));
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0; bus.WriteLong(Request + 0x28, Window);
        Assert.True(Invoke(bus, Device - 6, state));

        var output = new byte[] { 0x7F, 0x9B, (byte)'6', (byte)'n' };
        for (var index = 0; index < output.Length; index++) bus.WriteByte(Data + (uint)index, output[index], 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, (uint)output.Length);
        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Contains(draws, line => line.Contains((byte)0x7F));
        bus.WriteWord(Request + 0x1C, 2); bus.WriteLong(Request + 0x28, Data + 16); bus.WriteLong(Request + 0x24, 16);
        Assert.True(Invoke(bus, Device - 30, state));

        Assert.Equal("\u009B1;2R", System.Text.Encoding.Latin1.GetString(Enumerable.Range(0, 5).Select(index => bus.ReadByte(Data + 16u + (uint)index)).ToArray()));
    }

    [Fact]
    public void ConsoleTextUsesWindowPixelOriginAndFontBaseline()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        bus.WriteWord(Window + 0x08, 160); bus.WriteWord(Window + 0x0A, 100);
        bus.WriteWord(Window + 0x1A, 5); bus.WriteWord(Window + 0x1C, 6); bus.WriteWord(Window + 0x1E, 3); bus.WriteWord(Window + 0x20, 2);
        bus.WriteWord(RastPort + 0x3A, 10); bus.WriteWord(RastPort + 0x3C, 8); bus.WriteWord(RastPort + 0x3E, 8);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, _, _) => { });
        Assert.True(console.TryInstall(ExecBase));

        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0;
        bus.WriteLong(Request + 0x28, Window); Assert.True(Invoke(bus, Device - 6, state));
        bus.WriteByte(Data, (byte)'A', 10); bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, 1);

        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal((ushort)5, bus.ReadWord(RastPort + 0x24));
        Assert.Equal((ushort)14, bus.ReadWord(RastPort + 0x26));
    }

    [Fact]
    public void ConsolePrivateGeometryControlsRecalculatePageAndPixelOrigin()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        bus.WriteWord(Window + 0x08, 160); bus.WriteWord(Window + 0x0A, 100);
        bus.WriteWord(Window + 0x1A, 5); bus.WriteWord(Window + 0x1C, 6); bus.WriteWord(Window + 0x1E, 3); bus.WriteWord(Window + 0x20, 2);
        bus.WriteWord(RastPort + 0x3A, 10); bus.WriteWord(RastPort + 0x3C, 8); bus.WriteWord(RastPort + 0x3E, 8);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, _, _) => { });
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0; bus.WriteLong(Request + 0x28, Window);
        Assert.True(Invoke(bus, Device - 6, state));

        var output = new byte[] { 0x9B, (byte)'3', (byte)'0', (byte)'t', 0x9B, (byte)'1', (byte)'2', (byte)'u', 0x9B, (byte)'7', (byte)'x', 0x9B, (byte)'9', (byte)'y', (byte)'A' };
        for (var index = 0; index < output.Length; index++) bus.WriteByte(Data + (uint)index, output[index], 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, (uint)output.Length);

        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal((ushort)12, bus.ReadWord(RastPort + 0x24)); // border-left 5 + private left offset 7
        Assert.Equal((ushort)23, bus.ReadWord(RastPort + 0x26)); // border-top 6 + private top offset 9 + baseline 8

        var query = new byte[] { 0x9B, (byte)'0', (byte)'q' };
        for (var index = 0; index < query.Length; index++) bus.WriteByte(Data + (uint)index, query[index], 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, (uint)query.Length);
        Assert.True(Invoke(bus, Device - 30, state));
        bus.WriteWord(Request + 0x1C, 2); bus.WriteLong(Request + 0x28, Data + 32); bus.WriteLong(Request + 0x24, 32);
        Assert.True(Invoke(bus, Device - 30, state));
        const string expected = "\u009B1;1;3;12 r";
        Assert.Equal(expected, System.Text.Encoding.Latin1.GetString(Enumerable.Range(0, expected.Length).Select(index => bus.ReadByte(Data + 32 + (uint)index)).ToArray()));
    }

    [Fact]
    public void ConsolePrivateScrollControlKeepsTheBottomLineUntilReenabled()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        bus.WriteWord(Window + 0x08, 80); bus.WriteWord(Window + 0x0A, 20);
        bus.WriteWord(RastPort + 0x3A, 10); bus.WriteWord(RastPort + 0x3C, 8); bus.WriteWord(RastPort + 0x3E, 8);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        var draws = new List<string>();
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, text, length) => draws.Add(string.Concat(Enumerable.Range(0, (int)length).Select(index => (char)bus.ReadByte(text + (uint)index)))));
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0; bus.WriteLong(Request + 0x28, Window);
        Assert.True(Invoke(bus, Device - 6, state));

        var output = new byte[] { (byte)'A', (byte)'\n', (byte)'B', (byte)'\n', 0x9B, (byte)'>', (byte)'1', (byte)'l', (byte)'C', (byte)'\n', 0x9B, (byte)'>', (byte)'1', (byte)'h', (byte)'D', (byte)'\n' };
        for (var index = 0; index < output.Length; index++) bus.WriteByte(Data + (uint)index, output[index], 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, (uint)output.Length);
        Assert.True(Invoke(bus, Device - 30, state));

        draws.Clear(); bus.WriteWord(Request + 0x1C, 4); Assert.True(Invoke(bus, Device - 30, state));
        // LF preserves the current column.  While scrolling is disabled C
        // remains on the bottom row, so the later re-enabled scroll retains
        // the combined row instead of discarding it.
        Assert.Equal(new[] { "  CD" }, draws);
    }

    [Fact]
    public void ConsoleTabulationAndNewlineModesFollowTheirCsiControls()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        bus.WriteWord(Window + 0x08, 160); bus.WriteWord(Window + 0x0A, 80); bus.WriteWord(RastPort + 0x3A, 10); bus.WriteWord(RastPort + 0x3C, 8); bus.WriteWord(RastPort + 0x3E, 8);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, _, _) => { });
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0; bus.WriteLong(Request + 0x28, Window);
        Assert.True(Invoke(bus, Device - 6, state));

        // Set a tab at column five, return to it with CBT, then turn on
        // return-on-line-feed before emitting the final character.
        var output = new byte[] { 0x9B, (byte)'5', (byte)'G', 0x9B, (byte)'W', 0x9B, (byte)'1', (byte)'0', (byte)'G', 0x9B, (byte)'Z', (byte)'A', 0x9B, (byte)'2', (byte)'0', (byte)'h', (byte)'\n', (byte)'B' };
        for (var index = 0; index < output.Length; index++) bus.WriteByte(Data + (uint)index, output[index], 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, (uint)output.Length);

        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal((ushort)0, bus.ReadWord(RastPort + 0x24)); // LF reset the cursor to column zero before B
        Assert.Equal((ushort)18, bus.ReadWord(RastPort + 0x26)); // top 0 + baseline 8 + second row 10
    }

    [Fact]
    public void ConsoleBeginIoResolvesTheOpenedUnitFromIoUnit()
    {
        const uint secondRequest = 0x3D00;
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        var draws = new List<string>();
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, text, length) => draws.Add(string.Concat(Enumerable.Range(0, (int)length).Select(index => (char)bus.ReadByte(text + (uint)index)))));
        Assert.True(console.TryInstall(ExecBase));

        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0;
        bus.WriteLong(Request + 0x28, Window); Assert.True(Invoke(bus, Device - 6, state));
        bus.WriteLong(secondRequest + 0x14, Device); bus.WriteLong(secondRequest + 0x18, Request);
        bus.WriteByte(Data, (byte)'X', 10); bus.WriteWord(secondRequest + 0x1C, 3); bus.WriteLong(secondRequest + 0x28, Data); bus.WriteLong(secondRequest + 0x24, 1);
        state.A[1] = secondRequest;

        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal(new[] { "X" }, draws);
        Assert.Equal(1u, bus.ReadLong(secondRequest + 0x20));
    }

    [Fact]
    public void ConsoleSgrAppliesPensAndDrawModeToTheRenderedRun()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, _, _) => { });
        Assert.True(console.TryInstall(ExecBase));

        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0;
        bus.WriteLong(Request + 0x28, Window); Assert.True(Invoke(bus, Device - 6, state));
        var output = new byte[] { 0x1B, (byte)'[', (byte)'3', (byte)'1', (byte)';', (byte)'4', (byte)'4', (byte)'m', (byte)'X' };
        for (var index = 0; index < output.Length; index++) bus.WriteByte(Data + (uint)index, output[index], 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, (uint)output.Length);

        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal((byte)1, bus.ReadByte(RastPort + 0x19));
        Assert.Equal((byte)4, bus.ReadByte(RastPort + 0x1A));
        Assert.Equal((byte)2, bus.ReadByte(RastPort + 0x1C));
    }

    [Fact]
    public void ConsolePrivateDefaultSgrRestoresTheUserSelectedRendition()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, _, _) => { });
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0; bus.WriteLong(Request + 0x28, Window);
        Assert.True(Invoke(bus, Device - 6, state));

        // Set a preferred bold red-on-blue rendition, save it using the V39
        // private CSI SP s sequence, then reset with ordinary SGR 0.
        var output = new byte[] { 0x9B, (byte)'3', (byte)'3', (byte)';', (byte)'4', (byte)'4', (byte)';', (byte)'1', (byte)'m', 0x9B, (byte)' ', (byte)'s', 0x9B, (byte)'0', (byte)'m', (byte)'X' };
        for (var index = 0; index < output.Length; index++) bus.WriteByte(Data + (uint)index, output[index], 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, (uint)output.Length);

        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal((byte)3, bus.ReadByte(RastPort + 0x19));
        Assert.Equal((byte)4, bus.ReadByte(RastPort + 0x1A));
        Assert.Equal((byte)2, bus.ReadByte(RastPort + 0x1C));
    }

    [Fact]
    public void ConsoleConcealedSgrUsesTheBackgroundPenUntilItIsCleared()
    {
        const uint graphics = 0x3700;
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); InitializeLibrary(bus, graphics, 0x3780, "graphics.library"); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        var calls = new List<(uint Entry, uint Continuation)>();
        using var console = new ConsoleDeviceServices(new CopperStartConsoleContext(bus, memory, input, _ => { }, (_, _, _, _) => { }, (_, entry, continuation) => calls.Add((entry, continuation))));
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0; bus.WriteLong(Request + 0x28, Window);
        Assert.True(Invoke(bus, Device - 6, state));

        // A remains red on blue, B is concealed (blue on blue), then C is
        // visible again after SGR 28.  Separate model runs ensure redraws
        // retain the same distinction rather than only affecting live output.
        var output = new byte[] { 0x9B, (byte)'3', (byte)'1', (byte)';', (byte)'4', (byte)'4', (byte)'m', (byte)'A', 0x9B, (byte)'8', (byte)'m', (byte)'B', 0x9B, (byte)'2', (byte)'8', (byte)'m', (byte)'C' };
        for (var index = 0; index < output.Length; index++) bus.WriteByte(Data + (uint)index, output[index], 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, (uint)output.Length);

        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal((graphics - 0xEAu, 0x00F0_8930u), calls[0]);
        Assert.True(Invoke(bus, 0x00F0_8930, state));
        Assert.Equal((graphics - 0x3Cu, 0x00F0_8920u), calls[1]);
        Assert.Equal((byte)1, bus.ReadByte(RastPort + 0x19)); Assert.Equal((byte)4, bus.ReadByte(RastPort + 0x1A));
        Assert.True(Invoke(bus, 0x00F0_8920, state));
        Assert.Equal((graphics - 0x3Cu, 0x00F0_8920u), calls[2]);
        Assert.Equal((byte)4, bus.ReadByte(RastPort + 0x19)); Assert.Equal((byte)4, bus.ReadByte(RastPort + 0x1A));
        Assert.True(Invoke(bus, 0x00F0_8920, state));
        Assert.Equal((graphics - 0x3Cu, 0x00F0_8920u), calls[3]);
        Assert.Equal((byte)1, bus.ReadByte(RastPort + 0x19)); Assert.Equal((byte)4, bus.ReadByte(RastPort + 0x1A));
    }

    [Fact]
    public void ConsolePrivateSgrBackgroundPenIsUsedForSubsequentVisualClears()
    {
        const uint graphics = 0x3700;
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); InitializeLibrary(bus, graphics, 0x3780, "graphics.library"); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        var calls = new List<(uint Entry, uint Continuation)>();
        using var console = new ConsoleDeviceServices(new CopperStartConsoleContext(bus, memory, input, _ => { }, (_, _, _, _) => { }, (_, entry, continuation) => calls.Add((entry, continuation))));
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0; bus.WriteLong(Request + 0x28, Window);
        Assert.True(Invoke(bus, Device - 6, state));

        var output = new byte[] { 0x9B, (byte)'>', (byte)'5', (byte)'m', 0x0C };
        for (var index = 0; index < output.Length; index++) bus.WriteByte(Data + (uint)index, output[index], 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, (uint)output.Length);
        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal(new[] { (graphics - 0xEAu, 0x00F0_8930u) }, calls);
        Assert.Equal(5u, state.D[0]);
    }

    [Fact]
    public void ConsoleEscSaveAndRestoreRestoresTheRenditionAsWellAsTheCursor()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, _, _) => { });
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0; bus.WriteLong(Request + 0x28, Window);
        Assert.True(Invoke(bus, Device - 6, state));

        var output = new byte[] { 0x9B, (byte)'3', (byte)'2', (byte)';', (byte)'4', (byte)'4', (byte)'m', 0x1B, (byte)'7', 0x9B, (byte)'3', (byte)'1', (byte)'m', 0x1B, (byte)'8', (byte)'X' };
        for (var index = 0; index < output.Length; index++) bus.WriteByte(Data + (uint)index, output[index], 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, (uint)output.Length);

        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal((byte)2, bus.ReadByte(RastPort + 0x19)); Assert.Equal((byte)4, bus.ReadByte(RastPort + 0x1A));
    }

    [Fact]
    public void ConsoleBoldAndUnderlineUseOrderedNativeGraphicsOperations()
    {
        const uint graphics = 0x3700;
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); InitializeLibrary(bus, graphics, 0x3780, "graphics.library"); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        var calls = new List<(uint Entry, uint Continuation)>();
        using var console = new ConsoleDeviceServices(new CopperStartConsoleContext(bus, memory, input, _ => { }, (_, _, _, _) => { }, (_, entry, continuation) => calls.Add((entry, continuation))));
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0; bus.WriteLong(Request + 0x28, Window);
        Assert.True(Invoke(bus, Device - 6, state));
        var output = new byte[] { 0x9B, (byte)'1', (byte)';', (byte)'4', (byte)'m', (byte)'X' };
        for (var index = 0; index < output.Length; index++) bus.WriteByte(Data + (uint)index, output[index], 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, (uint)output.Length);

        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal(new[] { (graphics - 0xEAu, 0x00F0_8930u) }, calls);
        Assert.True(Invoke(bus, 0x00F0_8930, state));
        Assert.Equal((graphics - 0x3Cu, 0x00F0_8920u), calls[1]);
        Assert.True(Invoke(bus, 0x00F0_8920, state)); // bold pass
        Assert.Equal((graphics - 0x3Cu, 0x00F0_8920u), calls[2]);
        Assert.True(Invoke(bus, 0x00F0_8920, state)); // underline fill
        Assert.Equal((graphics - 0x132u, 0x00F0_8940u), calls[3]);
        Assert.Equal(0u, state.D[0]); Assert.Equal(8u, state.D[1]); Assert.Equal(7u, state.D[2]); Assert.Equal(8u, state.D[3]);
        Assert.True(Invoke(bus, 0x00F0_8940, state));
        Assert.Equal((graphics - 0x132u, 0x00F0_8940u), calls[4]); // visible cursor cell
        Assert.True(Invoke(bus, 0x00F0_8940, state));
    }

    [Fact]
    public void ConsoleItalicUsesAndThenRestoresGraphicsSoftStyle()
    {
        const uint graphics = 0x3700;
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); InitializeLibrary(bus, graphics, 0x3780, "graphics.library"); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        var calls = new List<(uint Entry, uint Continuation)>();
        using var console = new ConsoleDeviceServices(new CopperStartConsoleContext(bus, memory, input, _ => { }, (_, _, _, _) => { }, (_, entry, continuation) => calls.Add((entry, continuation))));
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0; bus.WriteLong(Request + 0x28, Window);
        Assert.True(Invoke(bus, Device - 6, state));

        var output = new byte[] { 0x9B, (byte)'3', (byte)'m', (byte)'X' };
        for (var index = 0; index < output.Length; index++) bus.WriteByte(Data + (uint)index, output[index], 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, (uint)output.Length);
        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal((graphics - 0xEAu, 0x00F0_8930u), calls[0]);
        Assert.True(Invoke(bus, 0x00F0_8930, state));
        Assert.Equal((graphics - 0x5Au, 0x00F0_8960u), calls[1]); Assert.Equal(4u, state.D[0]); Assert.Equal(4u, state.D[1]);
        Assert.True(Invoke(bus, 0x00F0_8960, state));
        Assert.Equal((graphics - 0x3Cu, 0x00F0_8920u), calls[2]);
        Assert.True(Invoke(bus, 0x00F0_8920, state));
        Assert.Equal((graphics - 0x5Au, 0x00F0_8968u), calls[3]); Assert.Equal(0u, state.D[0]); Assert.Equal(4u, state.D[1]);
        Assert.True(Invoke(bus, 0x00F0_8968, state));
        Assert.Equal((graphics - 0x132u, 0x00F0_8940u), calls[4]);
    }

    [Fact]
    public void AbortingAnItalicWriteStillRestoresTheGuestRastPortSoftStyle()
    {
        const uint graphics = 0x3700;
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); InitializeLibrary(bus, graphics, 0x3780, "graphics.library"); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        var replies = new List<uint>(); var calls = new List<(uint Entry, uint Continuation)>();
        using var console = new ConsoleDeviceServices(new CopperStartConsoleContext(bus, memory, input, replies.Add, (_, _, _, _) => { }, (_, entry, continuation) => calls.Add((entry, continuation))));
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0; bus.WriteLong(Request + 0x28, Window);
        Assert.True(Invoke(bus, Device - 6, state));

        var output = new byte[] { 0x9B, (byte)'3', (byte)'m', (byte)'X' };
        for (var index = 0; index < output.Length; index++) bus.WriteByte(Data + (uint)index, output[index], 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteByte(Request + 0x1E, 0, 10); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, (uint)output.Length);
        Assert.True(Invoke(bus, Device - 30, state));
        Assert.True(Invoke(bus, 0x00F0_8930, state)); Assert.True(Invoke(bus, 0x00F0_8960, state));
        state.A[1] = Request; Assert.True(Invoke(bus, Device - 36, state)); // AbortIO while Text() is in flight
        Assert.Equal(new[] { Request }, replies);
        Assert.True(Invoke(bus, 0x00F0_8920, state));
        Assert.Equal((graphics - 0x5Au, 0x00F0_8968u), calls[^1]);
        Assert.True(Invoke(bus, 0x00F0_8968, state));
        Assert.Equal(new[] { Request }, replies); // reset continuation never replies a second time
    }

    [Fact]
    public void ConsoleCursorRenditionReplaysThenRemovesTheVisibleCursor()
    {
        const uint graphics = 0x3700;
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); InitializeLibrary(bus, graphics, 0x3780, "graphics.library"); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        var calls = new List<(uint Entry, uint Continuation)>();
        using var console = new ConsoleDeviceServices(new CopperStartConsoleContext(bus, memory, input, _ => { }, (_, _, _, _) => { }, (_, entry, continuation) => calls.Add((entry, continuation))));
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0; bus.WriteLong(Request + 0x28, Window);
        Assert.True(Invoke(bus, Device - 6, state));

        bus.WriteByte(Data, (byte)'A', 10); bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, 1);
        Assert.True(Invoke(bus, Device - 30, state));
        Assert.True(Invoke(bus, 0x00F0_8930, state)); Assert.True(Invoke(bus, 0x00F0_8920, state));
        Assert.Equal((graphics - 0x132u, 0x00F0_8940u), calls[^1]);
        Assert.Equal(8u, state.D[0]); Assert.Equal(0u, state.D[1]); Assert.Equal(15u, state.D[2]); Assert.Equal(7u, state.D[3]);
        Assert.True(Invoke(bus, 0x00F0_8940, state));

        calls.Clear(); var hide = new byte[] { 0x9B, (byte)'0', (byte)' ', (byte)'p' };
        for (var index = 0; index < hide.Length; index++) bus.WriteByte(Data + (uint)index, hide[index], 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, (uint)hide.Length);
        state.A[1] = Request;
        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal(new[] { (graphics - 0xEAu, 0x00F0_8930u) }, calls);
        Assert.True(Invoke(bus, 0x00F0_8930, state));
        Assert.Equal((graphics - 0x3Cu, 0x00F0_8920u), calls[1]);
        Assert.True(Invoke(bus, 0x00F0_8920, state));
        Assert.Equal(2, calls.Count); // no final cursor RectFill after CSI 0 SP p
    }

    [Fact]
    public void ConsoleClearDiscardsQueuedInputWithoutErasingTheConsoleWindow()
    {
        const uint graphics = 0x3700;
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); InitializeLibrary(bus, graphics, 0x3780, "graphics.library"); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        var replies = new List<uint>(); var calls = new List<(uint Entry, uint Continuation)>();
        using var console = new ConsoleDeviceServices(new CopperStartConsoleContext(bus, memory, input, replies.Add, (_, _, _, _) => { }, (_, entry, continuation) => calls.Add((entry, continuation))));
        Assert.True(console.TryInstall(ExecBase));

        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0;
        bus.WriteLong(Request + 0x28, Window); Assert.True(Invoke(bus, Device - 6, state));
        bus.WriteByte(Event + 4, 1, 10); bus.WriteWord(Event + 6, 0x50, 10); // F1 report
        state.A[0] = Event; Assert.True(Invoke(bus, Device - 42, state)); console.ProcessPending(state);
        state.A[1] = Request;
        bus.WriteWord(Request + 0x1C, 5); bus.WriteByte(Request + 0x1E, 0, 10);

        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal(new[] { Request }, replies);
        Assert.Empty(calls);

        bus.WriteWord(Request + 0x1C, 2); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, 16);
        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal(0, bus.ReadByte(Request + 0x1E) & 1); // remains pending: F1 was discarded
    }

    [Fact]
    public void ConsoleFormFeedClearsTheWindowThroughTheNormalGraphicsContinuation()
    {
        const uint graphics = 0x3700;
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); InitializeLibrary(bus, graphics, 0x3780, "graphics.library"); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        var calls = new List<(uint Entry, uint Continuation)>();
        using var console = new ConsoleDeviceServices(new CopperStartConsoleContext(bus, memory, input, _ => { }, (_, _, _, _) => { }, (_, entry, continuation) => calls.Add((entry, continuation))));
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0;
        bus.WriteLong(Request + 0x28, Window); Assert.True(Invoke(bus, Device - 6, state));

        bus.WriteByte(Data, 0x0C, 10); bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, 1);
        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal(new[] { (graphics - 0xEAu, 0x00F0_8930u) }, calls);
        Assert.True(Invoke(bus, 0x00F0_8930, state));
        Assert.Equal((graphics - 0x132u, 0x00F0_8940u), calls[1]); // visible cursor after the clear
    }

    [Fact]
    public void ConsoleBellCallsNativeDisplayBeepBeforeCompletingTheWrite()
    {
        const uint intuition = 0x3700, intuitionName = 0x3780;
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); InitializeLibrary(bus, intuition, intuitionName, "intuition.library"); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        var replies = new List<uint>(); var calls = new List<(uint Entry, uint Continuation)>();
        using var console = new ConsoleDeviceServices(new CopperStartConsoleContext(bus, memory, input, replies.Add, (_, _, _, _) => { }, (_, entry, continuation) => calls.Add((entry, continuation))));
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0; bus.WriteLong(Request + 0x28, Window);
        Assert.True(Invoke(bus, Device - 6, state));

        bus.WriteByte(Data, 0x07, 10); bus.WriteWord(Request + 0x1C, 3); bus.WriteByte(Request + 0x1E, 0, 10); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, 1);
        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal(new[] { (intuition - 0x60u, 0x00F0_8950u) }, calls);
        Assert.Empty(replies);
        Assert.True(Invoke(bus, 0x00F0_8950, state));
        Assert.Equal(new[] { Request }, replies);
    }

    [Fact]
    public void ConsoleWriteSerializesMultipleBellsBeforeItsSingleCompletion()
    {
        const uint intuition = 0x3700, intuitionName = 0x3780;
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); InitializeLibrary(bus, intuition, intuitionName, "intuition.library"); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        var replies = new List<uint>(); var calls = new List<(uint Entry, uint Continuation)>();
        using var console = new ConsoleDeviceServices(new CopperStartConsoleContext(bus, memory, input, replies.Add, (_, _, _, _) => { }, (_, entry, continuation) => calls.Add((entry, continuation))));
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0; bus.WriteLong(Request + 0x28, Window);
        Assert.True(Invoke(bus, Device - 6, state));

        bus.WriteByte(Data, 0x07, 10); bus.WriteByte(Data + 1, 0x07, 10); bus.WriteWord(Request + 0x1C, 3); bus.WriteByte(Request + 0x1E, 0, 10); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, 2);
        Assert.True(Invoke(bus, Device - 30, state)); Assert.Single(calls); Assert.Empty(replies);
        Assert.True(Invoke(bus, 0x00F0_8950, state)); Assert.Equal(2, calls.Count); Assert.Empty(replies);
        Assert.True(Invoke(bus, 0x00F0_8950, state)); Assert.Equal(new[] { Request }, replies);
    }

    [Fact]
    public void ConsoleFlushAbortsAnOutstandingRenderBeforeCompleting()
    {
        const uint graphics = 0x3700, flushRequest = 0x3D00;
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); InitializeLibrary(bus, graphics, 0x3780, "graphics.library"); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        var replies = new List<uint>();
        using var console = new ConsoleDeviceServices(new CopperStartConsoleContext(bus, memory, input, replies.Add, (_, _, _, _) => { }, (_, _, _) => { }));
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0;
        bus.WriteLong(Request + 0x28, Window); Assert.True(Invoke(bus, Device - 6, state));
        bus.WriteByte(Data, (byte)'X', 10); bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, 1); bus.WriteByte(Request + 0x1E, 0, 10);
        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Empty(replies);

        bus.WriteLong(flushRequest + 0x14, Device); bus.WriteLong(flushRequest + 0x18, Request); bus.WriteWord(flushRequest + 0x1C, 8); bus.WriteByte(flushRequest + 0x1E, 0, 10); state.A[1] = flushRequest;
        Assert.True(Invoke(bus, Device - 30, state));

        Assert.Equal(new[] { Request, flushRequest }, replies);
        Assert.Equal((byte)0xFE, bus.ReadByte(Request + 0x1F));
        Assert.Equal((byte)0, bus.ReadByte(flushRequest + 0x1F));
    }

    [Fact]
    public void ConsoleResetAbortsAnOutstandingRenderBeforeStartingItsOwnClear()
    {
        const uint graphics = 0x3700, resetRequest = 0x3D00;
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); InitializeLibrary(bus, graphics, 0x3780, "graphics.library"); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        var replies = new List<uint>(); var calls = new List<(uint Entry, uint Continuation)>();
        using var console = new ConsoleDeviceServices(new CopperStartConsoleContext(bus, memory, input, replies.Add, (_, _, _, _) => { }, (_, entry, continuation) => calls.Add((entry, continuation))));
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0;
        bus.WriteLong(Request + 0x28, Window); Assert.True(Invoke(bus, Device - 6, state));

        bus.WriteByte(Data, (byte)'X', 10); bus.WriteWord(Request + 0x1C, 3); bus.WriteByte(Request + 0x1E, 0, 10); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, 1);
        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal(new[] { (graphics - 0xEAu, 0x00F0_8930u) }, calls);

        bus.WriteLong(resetRequest + 0x14, Device); bus.WriteLong(resetRequest + 0x18, Request); bus.WriteWord(resetRequest + 0x1C, 1); bus.WriteByte(resetRequest + 0x1E, 0, 10);
        state.A[1] = resetRequest; Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal(new[] { Request }, replies);
        Assert.Equal((byte)0xFE, bus.ReadByte(Request + 0x1F));
        Assert.Equal(2, calls.Count);
        Assert.Equal((graphics - 0xEAu, 0x00F0_8930u), calls[1]);

        Assert.True(Invoke(bus, 0x00F0_8930, state));
        Assert.Equal(new[] { Request, resetRequest }, replies);
        Assert.Equal((byte)0, bus.ReadByte(resetRequest + 0x1F));
    }

    [Fact]
    public void ConsoleSerializesWritesUntilTheirNativeTextContinuationsReturn()
    {
        const uint graphics = 0x3700, secondRequest = 0x3D00;
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); InitializeLibrary(bus, graphics, 0x3780, "graphics.library"); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        var replies = new List<uint>();
        using var console = new ConsoleDeviceServices(new CopperStartConsoleContext(bus, memory, input, replies.Add, (_, _, _, _) => { }, (_, _, _) => { }));
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0; bus.WriteLong(Request + 0x28, Window); Assert.True(Invoke(bus, Device - 6, state));
        bus.WriteLong(secondRequest + 0x14, Device); bus.WriteLong(secondRequest + 0x18, Request);
        bus.WriteByte(Data, (byte)'A', 10); bus.WriteByte(Data + 1, (byte)'B', 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, 1); bus.WriteByte(Request + 0x1E, 0, 10); Assert.True(Invoke(bus, Device - 30, state));
        state.A[1] = secondRequest; bus.WriteWord(secondRequest + 0x1C, 3); bus.WriteLong(secondRequest + 0x28, Data + 1); bus.WriteLong(secondRequest + 0x24, 1); bus.WriteByte(secondRequest + 0x1E, 0, 10); Assert.True(Invoke(bus, Device - 30, state));
        Assert.Empty(replies);

        Assert.True(Invoke(bus, 0x00F0_8930, state));
        Assert.True(Invoke(bus, 0x00F0_8920, state));
        Assert.True(Invoke(bus, 0x00F0_8940, state));
        Assert.Equal(new[] { Request }, replies);
        Assert.True(Invoke(bus, 0x00F0_8930, state));
        Assert.True(Invoke(bus, 0x00F0_8920, state));
        Assert.True(Invoke(bus, 0x00F0_8940, state));
        Assert.Equal(new[] { Request, secondRequest }, replies);
    }

    [Fact]
    public void ConsoleCsiCursorMovesAreClampedToTheUnitGeometry()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, _, _) => { });
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0;
        bus.WriteLong(Request + 0x28, Window); Assert.True(Invoke(bus, Device - 6, state));
        var output = System.Text.Encoding.ASCII.GetBytes("\x1b[999C\x1b[999B\x1b[6n");
        for (var index = 0; index < output.Length; index++) bus.WriteByte(Data + (uint)index, output[index], 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, (uint)output.Length); Assert.True(Invoke(bus, Device - 30, state));
        bus.WriteWord(Request + 0x1C, 2); bus.WriteLong(Request + 0x28, Data + 32); bus.WriteLong(Request + 0x24, 16); Assert.True(Invoke(bus, Device - 30, state));

        Assert.Equal("\u009B25;80R", System.Text.Encoding.Latin1.GetString(Enumerable.Range(0, 7).Select(index => bus.ReadByte(Data + 32u + (uint)index)).ToArray()));
    }

    [Fact]
    public void ConsoleScrollRepaintsTheVisibleTerminalModel()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        bus.WriteWord(Window + 0x08, 16); bus.WriteWord(Window + 0x0A, 10);
        bus.WriteWord(RastPort + 0x3A, 10); bus.WriteWord(RastPort + 0x3C, 8); bus.WriteWord(RastPort + 0x3E, 8);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        var draws = new List<string>();
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, text, length) => draws.Add(string.Concat(Enumerable.Range(0, (int)length).Select(index => (char)bus.ReadByte(text + (uint)index)))));
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0;
        bus.WriteLong(Request + 0x28, Window); Assert.True(Invoke(bus, Device - 6, state));
        var output = System.Text.Encoding.ASCII.GetBytes("A\r\nB");
        for (var index = 0; index < output.Length; index++) bus.WriteByte(Data + (uint)index, output[index], 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, (uint)output.Length);

        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal((uint)output.Length, bus.ReadLong(Request + 0x20));
        Assert.Equal(new[] { "B" }, draws);
    }

    [Fact]
    public void ConsoleResizeRequestsAReplayOfTheVisibleText()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        bus.WriteWord(Window + 0x08, 80); bus.WriteWord(Window + 0x0A, 40); bus.WriteWord(RastPort + 0x3A, 8); bus.WriteWord(RastPort + 0x3C, 8); bus.WriteWord(RastPort + 0x3E, 7);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        var draws = new List<string>();
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, text, length) => draws.Add(string.Concat(Enumerable.Range(0, (int)length).Select(index => (char)bus.ReadByte(text + (uint)index)))));
        Assert.True(console.TryInstall(ExecBase));
        bus.WriteLong(Window + 0x18, 0x40); // WFLG_SIMPLE_REFRESH
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 1; // CONU_CHARMAP owns redraw-on-resize
        bus.WriteLong(Request + 0x28, Window); Assert.True(Invoke(bus, Device - 6, state));
        bus.WriteByte(Data, (byte)'A', 10); bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, 1); Assert.True(Invoke(bus, Device - 30, state));
        draws.Clear();
        bus.WriteWord(Window + 0x08, 160);

        console.ProcessPending(state);

        Assert.Equal(new[] { "A" }, draws);
    }

    [Fact]
    public void StandardConsoleUpdatesGeometryWithoutReplayingOldTextOnResize()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        bus.WriteWord(Window + 0x08, 80); bus.WriteWord(Window + 0x0A, 40); bus.WriteWord(RastPort + 0x3A, 8); bus.WriteWord(RastPort + 0x3C, 8); bus.WriteWord(RastPort + 0x3E, 7);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        var draws = new List<string>();
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, text, length) => draws.Add(string.Concat(Enumerable.Range(0, (int)length).Select(index => (char)bus.ReadByte(text + (uint)index)))));
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0; // CONU_STANDARD
        bus.WriteLong(Request + 0x28, Window); Assert.True(Invoke(bus, Device - 6, state));
        bus.WriteByte(Data, (byte)'A', 10); bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, 1); Assert.True(Invoke(bus, Device - 30, state));
        draws.Clear(); bus.WriteWord(Window + 0x08, 160);

        console.ProcessPending(state);

        Assert.Empty(draws);
    }

    [Fact]
    public void CharacterMapNoDrawOnNewSizeFlagSuppressesItsAutomaticReplay()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        bus.WriteWord(Window + 0x08, 80); bus.WriteWord(Window + 0x0A, 40); bus.WriteWord(RastPort + 0x3A, 8); bus.WriteWord(RastPort + 0x3C, 8); bus.WriteWord(RastPort + 0x3E, 7);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        var draws = new List<string>();
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, text, length) => draws.Add(string.Concat(Enumerable.Range(0, (int)length).Select(index => (char)bus.ReadByte(text + (uint)index)))));
        Assert.True(console.TryInstall(ExecBase));
        bus.WriteLong(Window + 0x18, 0x40); // WFLG_SIMPLE_REFRESH
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 1; state.D[1] = 1; // CONU_CHARMAP | CONFLAG_NODRAW_ON_NEWSIZE
        bus.WriteLong(Request + 0x28, Window); Assert.True(Invoke(bus, Device - 6, state));
        bus.WriteByte(Data, (byte)'A', 10); bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, 1); Assert.True(Invoke(bus, Device - 30, state));
        draws.Clear(); bus.WriteWord(Window + 0x08, 160);

        console.ProcessPending(state);

        Assert.Empty(draws);
    }

    [Fact]
    public void ConsoleEditingCsiRepaintsTheModifiedTerminalModel()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        var draws = new List<string>();
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, text, length) => draws.Add(string.Concat(Enumerable.Range(0, (int)length).Select(index => (char)bus.ReadByte(text + (uint)index)))));
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0;
        bus.WriteLong(Request + 0x28, Window); Assert.True(Invoke(bus, Device - 6, state));

        foreach (var (index, value) in System.Text.Encoding.ASCII.GetBytes("AB").Select((value, index) => (index, value))) bus.WriteByte(Data + (uint)index, value, 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, 2); Assert.True(Invoke(bus, Device - 30, state));
        draws.Clear();
        var edit = System.Text.Encoding.ASCII.GetBytes("\x1b[1D\x1b[1P");
        for (var index = 0; index < edit.Length; index++) bus.WriteByte(Data + (uint)index, edit[index], 10);
        bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, (uint)edit.Length);

        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal(new[] { "A" }, draws);
    }

    [Fact]
    public void ConsoleInsertCharactersUsesSpacesAndClipsAtTheWindowRightEdge()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        bus.WriteWord(Window + 0x08, 32); bus.WriteWord(Window + 0x0A, 8); // four 8-pixel cells, one row
        bus.WriteWord(RastPort + 0x3A, 8); bus.WriteWord(RastPort + 0x3C, 8); bus.WriteWord(RastPort + 0x3E, 7);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        var draws = new List<string>();
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, text, length) => draws.Add(string.Concat(Enumerable.Range(0, (int)length).Select(index => (char)bus.ReadByte(text + (uint)index)))));
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0;
        bus.WriteLong(Request + 0x28, Window); Assert.True(Invoke(bus, Device - 6, state));

        var output = System.Text.Encoding.ASCII.GetBytes("AB\x1b[H\x1b[3@");
        for (var index = 0; index < output.Length; index++) bus.WriteByte(Data + (uint)index, output[index], 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, (uint)output.Length);
        Assert.True(Invoke(bus, Device - 30, state));

        Assert.Contains("   A", draws);
        Assert.DoesNotContain(draws, text => text.Contains('\0'));
        Assert.DoesNotContain(draws, text => text.Length > 4);
    }

    [Fact]
    public void ConsoleInsertAndDeleteLinePreserveTheBoundedScrollingRegion()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        bus.WriteWord(Window + 0x08, 80); bus.WriteWord(Window + 0x0A, 24); // 10 columns, three rows
        bus.WriteWord(RastPort + 0x3A, 8); bus.WriteWord(RastPort + 0x3C, 8); bus.WriteWord(RastPort + 0x3E, 7);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        var draws = new List<string>();
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, text, length) => draws.Add(string.Concat(Enumerable.Range(0, (int)length).Select(index => (char)bus.ReadByte(text + (uint)index)))));
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0; bus.WriteLong(Request + 0x28, Window);
        Assert.True(Invoke(bus, Device - 6, state));

        var output = System.Text.Encoding.ASCII.GetBytes("A\r\nB\r\nC\x1b[H\x1b[L");
        for (var index = 0; index < output.Length; index++) bus.WriteByte(Data + (uint)index, output[index], 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, (uint)output.Length);
        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal(new[] { "A", "B" }, draws.Where(text => text is "A" or "B").TakeLast(2));

        draws.Clear(); output = System.Text.Encoding.ASCII.GetBytes("\x1b[M");
        for (var index = 0; index < output.Length; index++) bus.WriteByte(Data + (uint)index, output[index], 10);
        bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, (uint)output.Length);
        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal(new[] { "A", "B" }, draws.Where(text => text is "A" or "B").TakeLast(2));
    }

    [Fact]
    public void ConsoleTabStopsCanBeClearedPerUnit()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, _, _) => { });
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0;
        bus.WriteLong(Request + 0x28, Window); Assert.True(Invoke(bus, Device - 6, state));
        var output = System.Text.Encoding.ASCII.GetBytes("\x1b[3g\t\x1b[6n");
        for (var index = 0; index < output.Length; index++) bus.WriteByte(Data + (uint)index, output[index], 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, (uint)output.Length); Assert.True(Invoke(bus, Device - 30, state));
        bus.WriteWord(Request + 0x1C, 2); bus.WriteLong(Request + 0x28, Data + 32); bus.WriteLong(Request + 0x24, 16); Assert.True(Invoke(bus, Device - 30, state));

        Assert.Equal("\u009B1;80R", System.Text.Encoding.Latin1.GetString(Enumerable.Range(0, 6).Select(index => bus.ReadByte(Data + 32u + (uint)index)).ToArray()));
    }

    [Fact]
    public void ConsoleClearDoesNotResetInputModesOrAbortReads()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, _, _) => { });
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0;
        bus.WriteLong(Request + 0x28, Window); Assert.True(Invoke(bus, Device - 6, state));
        foreach (var (index, value) in new byte[] { 0x9B, (byte)'1', (byte)'{' }.Select((value, index) => (index, value))) bus.WriteByte(Data + (uint)index, value, 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, 3); Assert.True(Invoke(bus, Device - 30, state));
        bus.WriteWord(Request + 0x1C, 5); Assert.True(Invoke(bus, Device - 30, state));
        bus.WriteWord(Request + 0x1C, 2); bus.WriteLong(Request + 0x28, Data + 32); bus.WriteLong(Request + 0x24, 32); Assert.True(Invoke(bus, Device - 30, state));
        bus.WriteByte(Event + 4, 1, 10); bus.WriteWord(Event + 6, 0x20); state.A[0] = Event; Assert.True(Invoke(bus, Device - 42, state)); console.ProcessPending(state);

        Assert.Equal((byte)0x9B, bus.ReadByte(Data + 32));
    }

    [Fact]
    public void ConsoleClearDiscardsRawEventsStagedBeforeTheNextBoundary()
    {
        const uint clearRequest = 0x3D00;
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, _, _) => { });
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0; bus.WriteLong(Request + 0x28, Window);
        Assert.True(Invoke(bus, Device - 6, state));
        bus.WriteByte(Event + 4, 1, 10); bus.WriteWord(Event + 6, 0x20); state.A[0] = Event;
        Assert.True(Invoke(bus, Device - 42, state));
        bus.WriteLong(clearRequest + 0x14, Device); bus.WriteLong(clearRequest + 0x18, Request); bus.WriteWord(clearRequest + 0x1C, 5); bus.WriteByte(clearRequest + 0x1E, 1, 10);
        state.A[1] = clearRequest; Assert.True(Invoke(bus, Device - 30, state));
        console.ProcessPending(state);

        state.A[1] = Request; bus.WriteWord(Request + 0x1C, 2); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, 1); bus.WriteByte(Request + 0x1E, 0, 10);
        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal(0, bus.ReadByte(Request + 0x1E) & 1);
    }

    [Fact]
    public void ConsoleResetRestoresOrdinaryKeyboardTranslation()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, _, _) => { });
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0;
        bus.WriteLong(Request + 0x28, Window); Assert.True(Invoke(bus, Device - 6, state));
        foreach (var (index, value) in new byte[] { 0x9B, (byte)'1', (byte)'{' }.Select((value, index) => (index, value))) bus.WriteByte(Data + (uint)index, value, 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, 3); Assert.True(Invoke(bus, Device - 30, state));
        bus.WriteWord(Request + 0x1C, 1); Assert.True(Invoke(bus, Device - 30, state));
        bus.WriteWord(Request + 0x1C, 2); bus.WriteLong(Request + 0x28, Data + 32); bus.WriteLong(Request + 0x24, 8); Assert.True(Invoke(bus, Device - 30, state));
        bus.WriteByte(Event + 4, 1, 10); bus.WriteWord(Event + 6, 0x20); state.A[0] = Event; Assert.True(Invoke(bus, Device - 42, state)); console.ProcessPending(state);

        Assert.Equal((byte)'a', bus.ReadByte(Data + 32));
    }

    [Fact]
    public void ConsoleRisEscapeResetsRawInputMode()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, _, _) => { });
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0;
        bus.WriteLong(Request + 0x28, Window); Assert.True(Invoke(bus, Device - 6, state));
        var output = new byte[] { 0x9B, (byte)'1', (byte)'{', 0x1B, (byte)'c' };
        for (var index = 0; index < output.Length; index++) bus.WriteByte(Data + (uint)index, output[index], 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, (uint)output.Length); Assert.True(Invoke(bus, Device - 30, state));
        bus.WriteWord(Request + 0x1C, 2); bus.WriteLong(Request + 0x28, Data + 32); bus.WriteLong(Request + 0x24, 8); Assert.True(Invoke(bus, Device - 30, state));
        bus.WriteByte(Event + 4, 1, 10); bus.WriteWord(Event + 6, 0x20); state.A[0] = Event; Assert.True(Invoke(bus, Device - 42, state)); console.ProcessPending(state);

        Assert.Equal((byte)'a', bus.ReadByte(Data + 32));
    }

    [Fact]
    public void ConsoleRisAlsoResetsSavedRenditionBeforeRestoreCursor()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        var draws = new List<byte>();
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, rastPort, _, _) => draws.Add(bus.ReadByte(rastPort + 0x19)));
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0; bus.WriteLong(Request + 0x28, Window);
        Assert.True(Invoke(bus, Device - 6, state));

        var output = new byte[] { 0x1B, (byte)'[', (byte)'3', (byte)'3', (byte)'m', 0x1B, (byte)'7', 0x1B, (byte)'c', 0x1B, (byte)'8', (byte)'X' };
        for (var index = 0; index < output.Length; index++) bus.WriteByte(Data + (uint)index, output[index], 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, (uint)output.Length);
        Assert.True(Invoke(bus, Device - 30, state));

        Assert.Contains((byte)1, draws);
        Assert.DoesNotContain((byte)3, draws);
    }

    [Fact]
    public void ConsoleUpdateRepaintsTheCurrentPage()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        var draws = new List<string>();
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, text, length) => draws.Add(string.Concat(Enumerable.Range(0, (int)length).Select(index => (char)bus.ReadByte(text + (uint)index)))));
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0;
        bus.WriteLong(Request + 0x28, Window); Assert.True(Invoke(bus, Device - 6, state));
        bus.WriteByte(Data, (byte)'A', 10); bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, 1); Assert.True(Invoke(bus, Device - 30, state));
        draws.Clear();
        bus.WriteWord(Request + 0x1C, 4);

        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal(new[] { "A" }, draws);
    }

    [Fact]
    public void ConsoleUpdatePreservesSgrStylesFromTheTerminalModel()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        var draws = new List<(string Text, byte Pen)>();
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, rastPort, text, length) => draws.Add((string.Concat(Enumerable.Range(0, (int)length).Select(index => (char)bus.ReadByte(text + (uint)index))), bus.ReadByte(rastPort + 0x19))));
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0;
        bus.WriteLong(Request + 0x28, Window); Assert.True(Invoke(bus, Device - 6, state));
        var output = System.Text.Encoding.ASCII.GetBytes("\x1b[31mA\x1b[32mB");
        for (var index = 0; index < output.Length; index++) bus.WriteByte(Data + (uint)index, output[index], 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, (uint)output.Length); Assert.True(Invoke(bus, Device - 30, state));
        draws.Clear();
        bus.WriteWord(Request + 0x1C, 4); Assert.True(Invoke(bus, Device - 30, state));

        Assert.Equal(new[] { ("A", (byte)1), ("B", (byte)2) }, draws);
    }

    [Fact]
    public void ConsoleFaintUsesTheSecondaryCellPenUntilNormalColourIsSelected()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        var draws = new List<(string Text, byte Pen)>();
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, rastPort, text, length) => draws.Add((string.Concat(Enumerable.Range(0, (int)length).Select(index => (char)bus.ReadByte(text + (uint)index))), bus.ReadByte(rastPort + 0x19))));
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0; bus.WriteLong(Request + 0x28, Window);
        Assert.True(Invoke(bus, Device - 6, state));

        var output = System.Text.Encoding.ASCII.GetBytes("\x1b[33;40;2mX\x1b[22mY");
        for (var index = 0; index < output.Length; index++) bus.WriteByte(Data + (uint)index, output[index], 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, (uint)output.Length);
        Assert.True(Invoke(bus, Device - 30, state));

        Assert.Equal(new[] { ("X", (byte)0), ("Y", (byte)3) }, draws.Where(draw => draw.Text is "X" or "Y"));
    }

    [Fact]
    public void ConsoleOpenEnforcesWindowAndUnitRules()
    {
        const uint noWindowRequest = 0x3D00, invalidUnitRequest = 0x3E00, characterMapRequest = 0x3F00;
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, _, _) => { });
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 };

        state.A[1] = Request; state.D[0] = uint.MaxValue; bus.WriteLong(Request + 0x28, 0); Assert.True(Invoke(bus, Device - 6, state)); Assert.Equal(0u, state.D[0]);
        Assert.Equal(Device, bus.ReadLong(Request + 0x14)); Assert.Equal(0u, bus.ReadLong(Request + 0x18));
        state.D[0] = uint.MaxValue; Assert.True(Invoke(bus, Device - 6, state)); Assert.Equal(0xFFu, state.D[0]);
        state.A[1] = noWindowRequest; state.D[0] = 0; bus.WriteLong(noWindowRequest + 0x28, 0); Assert.True(Invoke(bus, Device - 6, state)); Assert.Equal(0xFFu, state.D[0]);
        state.A[1] = invalidUnitRequest; state.D[0] = 2; bus.WriteLong(invalidUnitRequest + 0x28, Window); Assert.True(Invoke(bus, Device - 6, state)); Assert.Equal(0xFFu, state.D[0]);
        state.A[1] = characterMapRequest; state.D[0] = 1; bus.WriteLong(characterMapRequest + 0x28, Window); Assert.True(Invoke(bus, Device - 6, state)); Assert.Equal(0xFFu, state.D[0]);
        bus.WriteLong(Window + 0x18, 0x40); state.D[0] = 1; Assert.True(Invoke(bus, Device - 6, state)); Assert.Equal(0u, state.D[0]);
    }

    [Fact]
    public void ConsoleDirectGatewaysAreRemovedWithoutChangingUnderlyingVectors()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus);
        var originalBeginIo = bus.ReadWord(Device - 30);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, _, _) => { });

        Assert.True(console.TryInstall(ExecBase));
        Assert.Equal((ushort)0xFF00, bus.ReadWord(Device - 30));
        console.Reset();

        Assert.False(bus.HasHostGateway(Device - 30));
        Assert.Equal(originalBeginIo, bus.ReadWord(Device - 30));
    }

    [Fact]
    public void ConsoleSnipMapUnitReportsPasteForRightAmigaV()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, _, _) => { });
        Assert.True(console.TryInstall(ExecBase));
        bus.WriteLong(Window + 0x18, 0x40); // WFLG_SIMPLE_REFRESH
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 3; bus.WriteLong(Request + 0x28, Window); Assert.True(Invoke(bus, Device - 6, state));
        bus.WriteByte(Event + 4, 1, 10); bus.WriteWord(Event + 6, 0x34); bus.WriteWord(Event + 8, 0x80); state.A[0] = Event; Assert.True(Invoke(bus, Device - 42, state)); console.ProcessPending(state);
        bus.WriteWord(Request + 0x1C, 2); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, 8); Assert.True(Invoke(bus, Device - 30, state));

        Assert.Equal("\u009B0 v", System.Text.Encoding.Latin1.GetString(Enumerable.Range(0, 4).Select(index => bus.ReadByte(Data + (uint)index)).ToArray()));
    }

    [Fact]
    public void ConsoleSnipMapPastesThroughGuestClipboardDeviceWhenAvailable()
    {
        const uint clipboardRequest = 0x7000, clipboardBuffer = 0x7100;
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)((bytes + 7) & ~7); return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        using var clipboard = new ClipboardDeviceServices(bus, memory, _ => { }, (_, _, _) => { }, (_, _, _) => { }, 0xF08930);
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, _, _) => { });
        Assert.True(clipboard.TryInstall(ExecBase)); Assert.True(console.TryInstall(ExecBase));

        var clipboardText = ClipboardIffText.Encode("paste");
        for (var index = 0; index < clipboardText.Length; index++) bus.WriteByte(clipboardBuffer + (uint)index, clipboardText[index], 10);
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = clipboardRequest; state.D[0] = 0;
        Assert.True(Invoke(bus, clipboard.DeviceBase - 6, state));
        bus.WriteWord(clipboardRequest + 0x1C, 3); bus.WriteByte(clipboardRequest + 0x1E, 1, 10); bus.WriteLong(clipboardRequest + 0x28, clipboardBuffer); bus.WriteLong(clipboardRequest + 0x24, (uint)clipboardText.Length); bus.WriteLong(clipboardRequest + 0x2C, 0); bus.WriteLong(clipboardRequest + 0x30, 0);
        Assert.True(Invoke(bus, clipboard.DeviceBase - 30, state));
        var clipId = bus.ReadLong(clipboardRequest + 0x30);
        bus.WriteWord(clipboardRequest + 0x1C, 4); bus.WriteLong(clipboardRequest + 0x30, clipId); Assert.True(Invoke(bus, clipboard.DeviceBase - 30, state));

        bus.WriteLong(Window + 0x18, 0x40); state.A[1] = Request; state.D[0] = 3; bus.WriteLong(Request + 0x28, Window); Assert.True(Invoke(bus, Device - 6, state));
        bus.WriteByte(Event + 4, 1, 10); bus.WriteWord(Event + 6, 0x34); bus.WriteWord(Event + 8, 0x80); state.A[0] = Event;
        Assert.True(Invoke(bus, Device - 42, state)); console.ProcessPending(state);
        state.A[1] = Request; bus.WriteWord(Request + 0x1C, 2); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, 8); Assert.True(Invoke(bus, Device - 30, state));

        Assert.Equal("paste", System.Text.Encoding.Latin1.GetString(Enumerable.Range(0, 5).Select(index => bus.ReadByte(Data + (uint)index)).ToArray()));
    }

    [Fact]
    public void StandardConsoleLeavesRightAmigaVToTheGuestKeymapInsteadOfReadingClipboard()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, _, _) => { });
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0; bus.WriteLong(Request + 0x28, Window);
        Assert.True(Invoke(bus, Device - 6, state));

        bus.WriteByte(Event + 4, 1, 10); bus.WriteWord(Event + 6, 0x34); bus.WriteWord(Event + 8, 0x80); state.A[0] = Event;
        Assert.True(Invoke(bus, Device - 42, state)); console.ProcessPending(state);
        state.A[1] = Request; bus.WriteWord(Request + 0x1C, 2); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, 8);
        Assert.True(Invoke(bus, Device - 30, state));

        Assert.Equal((byte)'v', bus.ReadByte(Data));
        Assert.NotEqual((byte)0x9B, bus.ReadByte(Data));
    }

    [Fact]
    public void ConsoleCopyUsesGuestClipboardDeviceIo()
    {
        const uint clipboardRequest = 0x7000, clipboardBuffer = 0x7100;
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000;
        var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)((bytes + 7) & ~7); return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        using var clipboard = new ClipboardDeviceServices(bus, memory, _ => { }, (_, _, _) => { }, (_, _, _) => { }, 0xF08930);
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, _, _) => { });
        Assert.True(clipboard.TryInstall(ExecBase)); Assert.True(console.TryInstall(ExecBase));

        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0; bus.WriteLong(Request + 0x28, Window);
        Assert.True(Invoke(bus, Device - 6, state));
        bus.WriteByte(Data, (byte)'c', 10); bus.WriteByte(Data + 1, (byte)'o', 10); bus.WriteByte(Data + 2, (byte)'p', 10); bus.WriteByte(Data + 3, (byte)'y', 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, 4);
        Assert.True(Invoke(bus, Device - 30, state));

        bus.WriteByte(Event + 4, 1, 10); bus.WriteWord(Event + 6, 0x33); bus.WriteWord(Event + 8, 0x80); state.A[0] = Event;
        Assert.True(Invoke(bus, Device - 42, state)); console.ProcessPending(state);

        state.A[1] = clipboardRequest; state.D[0] = 0;
        Assert.True(Invoke(bus, clipboard.DeviceBase - 6, state));
        bus.WriteWord(clipboardRequest + 0x1C, 2); bus.WriteByte(clipboardRequest + 0x1E, 1, 10); bus.WriteLong(clipboardRequest + 0x28, clipboardBuffer); bus.WriteLong(clipboardRequest + 0x24, 128); bus.WriteLong(clipboardRequest + 0x2C, 0); bus.WriteLong(clipboardRequest + 0x30, 0);
        Assert.True(Invoke(bus, clipboard.DeviceBase - 30, state));
        var count = bus.ReadLong(clipboardRequest + 0x20);
        Assert.True(ClipboardIffText.TryDecode(Enumerable.Range(0, (int)count).Select(index => bus.ReadByte(clipboardBuffer + (uint)index)).ToArray(), out var text));
        Assert.Equal("copy", text);
    }

    [Fact]
    public void ConsoleSnipMapCopiesOnlyTheMouseSelectedGuestText()
    {
        const uint clipboardRequest = 0x7000, clipboardBuffer = 0x7100;
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000;
        var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)((bytes + 7) & ~7); return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        using var clipboard = new ClipboardDeviceServices(bus, memory, _ => { }, (_, _, _) => { }, (_, _, _) => { }, 0xF08930);
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, _, _) => { });
        Assert.True(clipboard.TryInstall(ExecBase)); Assert.True(console.TryInstall(ExecBase));

        bus.WriteLong(Window + 0x18, 0x40); // WFLG_SIMPLE_REFRESH
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 3; bus.WriteLong(Request + 0x28, Window);
        Assert.True(Invoke(bus, Device - 6, state));
        var output = System.Text.Encoding.ASCII.GetBytes("one");
        for (var index = 0; index < output.Length; index++) bus.WriteByte(Data + (uint)index, output[index], 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, (uint)output.Length);
        Assert.True(Invoke(bus, Device - 30, state));

        void SendMouse(ushort code, short x, short y)
        {
            bus.WriteByte(Event + 4, 2, 10); bus.WriteWord(Event + 6, code, 10); bus.WriteWord(Event + 10, unchecked((ushort)x), 10); bus.WriteWord(Event + 12, unchecked((ushort)y), 10);
            state.A[0] = Event; Assert.True(Invoke(bus, Device - 42, state)); console.ProcessPending(state);
        }
        SendMouse(0x68, 0, 0); // select at the beginning of "one"
        SendMouse(0xFF, 24, 0); // drag three 8-pixel cells
        Assert.Equal((byte)0, bus.ReadByte(RastPort + 0x19)); // selection replay inverts the stored cell style
        Assert.Equal((byte)1, bus.ReadByte(RastPort + 0x1A));
        SendMouse(0xE8, 0, 0);

        bus.WriteByte(Event + 4, 1, 10); bus.WriteWord(Event + 6, 0x33, 10); bus.WriteWord(Event + 8, 0x80, 10);
        state.A[0] = Event; Assert.True(Invoke(bus, Device - 42, state)); console.ProcessPending(state);
        state.A[1] = clipboardRequest; state.D[0] = 0; Assert.True(Invoke(bus, clipboard.DeviceBase - 6, state));
        bus.WriteWord(clipboardRequest + 0x1C, 2); bus.WriteByte(clipboardRequest + 0x1E, 1, 10); bus.WriteLong(clipboardRequest + 0x28, clipboardBuffer); bus.WriteLong(clipboardRequest + 0x24, 128); bus.WriteLong(clipboardRequest + 0x2C, 0); bus.WriteLong(clipboardRequest + 0x30, 0);
        Assert.True(Invoke(bus, clipboard.DeviceBase - 30, state));
        var count = bus.ReadLong(clipboardRequest + 0x20);
        Assert.True(ClipboardIffText.TryDecode(Enumerable.Range(0, (int)count).Select(index => bus.ReadByte(clipboardBuffer + (uint)index)).ToArray(), out var text));
        Assert.Equal("one", text);
    }

    [Fact]
    public void ConsoleSnipMapSwallowsRightAmigaCWhenThereIsNoSelection()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort); bus.WriteLong(Window + 0x18, 0x40); // SIMPLE_REFRESH
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, _, _) => { });
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 3; bus.WriteLong(Request + 0x28, Window);
        Assert.True(Invoke(bus, Device - 6, state));

        bus.WriteByte(Event + 4, 1, 10); bus.WriteWord(Event + 6, 0x33, 10); bus.WriteWord(Event + 8, 0x0080, 10);
        state.A[0] = Event; Assert.True(Invoke(bus, Device - 42, state)); console.ProcessPending(state);

        // The no-op shortcut must not reach normal guest keymap translation
        // as a literal 'c'.
        state.A[1] = Request; bus.WriteWord(Request + 0x1C, 2); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, 1); bus.WriteByte(Request + 0x1E, 0, 10);
        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal(0, bus.ReadByte(Request + 0x1E) & 1);
    }

    [Fact]
    public void ConsoleRawKeyboardSelectionReportsTheDocumentedCsiEventRecord()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, _, _) => { });
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0; bus.WriteLong(Request + 0x28, Window); Assert.True(Invoke(bus, Device - 6, state));
        var select = new byte[] { 0x9B, (byte)'1', (byte)'{' };
        for (var index = 0; index < select.Length; index++) bus.WriteByte(Data + (uint)index, select[index], 10);
        bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, (uint)select.Length); Assert.True(Invoke(bus, Device - 30, state));
        bus.WriteByte(Event + 4, 1, 10); bus.WriteWord(Event + 6, 0x0020); bus.WriteWord(Event + 8, 3); bus.WriteWord(Event + 10, 7); bus.WriteWord(Event + 12, unchecked((ushort)-2)); bus.WriteLong(Event + 14, 12); bus.WriteLong(Event + 18, 34); state.A[0] = Event; Assert.True(Invoke(bus, Device - 42, state)); console.ProcessPending(state);
        bus.WriteWord(Request + 0x1C, 2); bus.WriteLong(Request + 0x28, Data + 16); bus.WriteLong(Request + 0x24, 64); Assert.True(Invoke(bus, Device - 30, state));
        const string expected = "1;0;32;3;7;-2;12;34|";
        Assert.Equal((byte)0x9B, bus.ReadByte(Data + 16)); Assert.Equal(expected, System.Text.Encoding.ASCII.GetString(Enumerable.Range(17, expected.Length).Select(index => bus.ReadByte(Data + (uint)index)).ToArray()));
    }

    [Fact]
    public void CdInputHandlerConsumesConsoleEventsAndReturnsOnlyTheUnusedChain()
    {
        const uint unused = 0x3D00;
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, _, _) => { });
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0;
        bus.WriteLong(Request + 0x28, Window); Assert.True(Invoke(bus, Device - 6, state));

        // A raw key belongs to the opened console; an unrelated event stays
        // in the handler chain for downstream input handlers.
        bus.WriteLong(Event, unused); bus.WriteByte(Event + 4, 1, 10); bus.WriteWord(Event + 6, 0x20, 10);
        bus.WriteLong(unused, 0); bus.WriteByte(unused + 4, 0x7E, 10);
        state.A[0] = Event; Assert.True(Invoke(bus, Device - 42, state));
        Assert.Equal(unused, state.D[0]); Assert.Equal(0u, bus.ReadLong(unused));

        console.ProcessPending(state);
        state.A[1] = Request; bus.WriteWord(Request + 0x1C, 2); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, 1);
        Assert.True(Invoke(bus, Device - 30, state)); Assert.Equal((byte)'a', bus.ReadByte(Data));
    }

    [Fact]
    public void ConsoleReportsFunctionAndCursorKeysWithoutPassingThemToTheKeyMap()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, _, _) => { });
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0; bus.WriteLong(Request + 0x28, Window);
        Assert.True(Invoke(bus, Device - 6, state));

        void SendRawKey(ushort code, ushort qualifier = 0)
        {
            bus.WriteByte(Event + 4, 1, 10); bus.WriteWord(Event + 6, code, 10); bus.WriteWord(Event + 8, qualifier, 10);
            state.A[0] = Event; Assert.True(Invoke(bus, Device - 42, state)); console.ProcessPending(state);
        }

        SendRawKey(0x50); // F1
        SendRawKey(0x50, 1); // Shift-F1
        SendRawKey(0x47); // Insert
        SendRawKey(0x48, 1); // Shift-PageUp
        SendRawKey(0x49); // PageDown
        SendRawKey(0x4F, 1); // Shift-cursor-left
        bus.WriteWord(Request + 0x1C, 2); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, 32);
        Assert.True(Invoke(bus, Device - 30, state));
        const string expected = "\u009B0~\u009B10~\u009B40~\u009B51~\u009B42~\u009B A";
        Assert.Equal(expected, System.Text.Encoding.Latin1.GetString(Enumerable.Range(0, expected.Length).Select(index => bus.ReadByte(Data + (uint)index)).ToArray()));
    }

    [Fact]
    public void ConsoleRoutesCookedKeyboardInputOnlyToTheActiveWindowUnit()
    {
        const uint intuition = 0x3700, intuitionName = 0x3780, secondRequest = 0x3D00, secondWindow = 0x3800, secondRastPort = 0x3900, secondData = 0x3E00;
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); InitializeLibrary(bus, intuition, intuitionName, "intuition.library"); bus.WriteLong(Window + 0x32, RastPort); bus.WriteLong(secondWindow + 0x32, secondRastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, _, _) => { });
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0; bus.WriteLong(Request + 0x28, Window);
        Assert.True(Invoke(bus, Device - 6, state));
        bus.WriteLong(secondRequest + 0x14, Device); state.A[1] = secondRequest; state.D[0] = 0; bus.WriteLong(secondRequest + 0x28, secondWindow);
        Assert.True(Invoke(bus, Device - 6, state)); // newest window becomes active by default
        bus.WriteLong(intuition + 0x34, Window); // native Intuition makes the first console foreground

        bus.WriteByte(Event + 4, 1, 10); bus.WriteWord(Event + 6, 0x20, 10); state.A[0] = Event;
        Assert.True(Invoke(bus, Device - 42, state));
        bus.WriteLong(intuition + 0x34, secondWindow); // focus changes after the event was observed
        console.ProcessPending(state);

        state.A[1] = Request; bus.WriteWord(Request + 0x1C, 2); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, 1);
        Assert.True(Invoke(bus, Device - 30, state)); Assert.Equal((byte)'a', bus.ReadByte(Data));
        state.A[1] = secondRequest; bus.WriteWord(secondRequest + 0x1C, 2); bus.WriteLong(secondRequest + 0x28, secondData); bus.WriteLong(secondRequest + 0x24, 1);
        Assert.True(Invoke(bus, Device - 30, state)); Assert.Equal(0, bus.ReadByte(secondRequest + 0x1E) & 1);
    }

    [Fact]
    public void ConsoleRoutesWindowSpecificRawReportsToTheirOwningUnit()
    {
        const uint intuition = 0x3700, intuitionName = 0x3780, secondRequest = 0x3D00, secondWindow = 0x3800, secondRastPort = 0x3900, secondData = 0x3E00;
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); InitializeLibrary(bus, intuition, intuitionName, "intuition.library"); bus.WriteLong(Window + 0x32, RastPort); bus.WriteLong(secondWindow + 0x32, secondRastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, _, _) => { });
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0; bus.WriteLong(Request + 0x28, Window);
        Assert.True(Invoke(bus, Device - 6, state));
        bus.WriteLong(secondRequest + 0x14, Device); state.A[1] = secondRequest; state.D[0] = 0; bus.WriteLong(secondRequest + 0x28, secondWindow);
        Assert.True(Invoke(bus, Device - 6, state));

        // Request window-resized reports from the first unit, while the
        // second window is the active Intuition window.
        var sequence = new byte[] { 0x9B, (byte)'1', (byte)'2', (byte)'{' };
        for (var index = 0; index < sequence.Length; index++) bus.WriteByte(Data + (uint)index, sequence[index], 10);
        state.A[1] = Request; bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, (uint)sequence.Length);
        Assert.True(Invoke(bus, Device - 30, state));
        bus.WriteLong(intuition + 0x34, secondWindow);
        bus.WriteByte(Event + 4, 12, 10); bus.WriteWord(Event + 10, (ushort)(Window >> 16), 10); bus.WriteWord(Event + 12, (ushort)Window, 10); state.A[0] = Event;
        Assert.True(Invoke(bus, Device - 42, state)); console.ProcessPending(state);

        state.A[1] = Request; bus.WriteWord(Request + 0x1C, 2); bus.WriteLong(Request + 0x28, Data + 32); bus.WriteLong(Request + 0x24, 32);
        Assert.True(Invoke(bus, Device - 30, state));
        Assert.StartsWith("\u009B12;", System.Text.Encoding.Latin1.GetString(Enumerable.Range(0, 4).Select(index => bus.ReadByte(Data + 32u + (uint)index)).ToArray()));
        state.A[1] = secondRequest; bus.WriteWord(secondRequest + 0x1C, 2); bus.WriteLong(secondRequest + 0x28, secondData); bus.WriteLong(secondRequest + 0x24, 1);
        Assert.True(Invoke(bus, Device - 30, state)); Assert.Equal(0, bus.ReadByte(secondRequest + 0x1E) & 1);
    }

    [Fact]
    public void CdInputHandlerDoesNotConsumeARawEventRequestedByAnotherConsoleWindow()
    {
        const uint intuition = 0x3700, intuitionName = 0x3780, secondRequest = 0x3D00, secondWindow = 0x3800, secondRastPort = 0x3900;
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); InitializeLibrary(bus, intuition, intuitionName, "intuition.library"); bus.WriteLong(Window + 0x32, RastPort); bus.WriteLong(secondWindow + 0x32, secondRastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, _, _) => { });
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0; bus.WriteLong(Request + 0x28, Window);
        Assert.True(Invoke(bus, Device - 6, state));
        bus.WriteLong(secondRequest + 0x14, Device); state.A[1] = secondRequest; state.D[0] = 0; bus.WriteLong(secondRequest + 0x28, secondWindow);
        Assert.True(Invoke(bus, Device - 6, state));

        // Only the first console asks for NEWSIZE events.  The second
        // window's event stays available to downstream input handlers.
        var requestRaw = new byte[] { 0x9B, (byte)'1', (byte)'2', (byte)'{' };
        for (var index = 0; index < requestRaw.Length; index++) bus.WriteByte(Data + (uint)index, requestRaw[index], 10);
        state.A[1] = Request; bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, (uint)requestRaw.Length);
        Assert.True(Invoke(bus, Device - 30, state));

        bus.WriteLong(Event, 0); bus.WriteByte(Event + 4, 12, 10); bus.WriteWord(Event + 10, (ushort)(secondWindow >> 16), 10); bus.WriteWord(Event + 12, (ushort)secondWindow, 10);
        state.A[0] = Event; Assert.True(Invoke(bus, Device - 42, state));
        Assert.Equal(Event, state.D[0]);
    }

    [Fact]
    public void RawKeyConvertEntersTheCurrentGuestKeyMapAndReportsOverflow()
    {
        const uint keyMapLibrary = 0x3700, keyMap = 0x3D00;
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); InitializeLibrary(bus, keyMapLibrary, 0x3780, "keymap.library");
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        var calls = new List<(uint Entry, uint Continuation)>();
        using var console = new ConsoleDeviceServices(new CopperStartConsoleContext(bus, memory, input, _ => { }, (_, _, _, _) => { }, (_, entry, continuation) => calls.Add((entry, continuation))));
        Assert.True(console.TryInstall(ExecBase));

        var state = new M68kCpuState { Cycles = 10 }; state.A[0] = Event; state.A[1] = Data; state.A[2] = keyMap; state.D[1] = 4;
        bus.WriteByte(Event + 4, 1, 10); bus.WriteWord(Event + 6, 0x20, 10);

        Assert.True(Invoke(bus, Device - 48, state));
        Assert.Equal(new[] { (keyMapLibrary - 36u, 0x00F0_8918u) }, calls);
        Assert.Equal(keyMap, state.A[2]);
        state.D[0] = 8;
        Assert.True(Invoke(bus, 0x00F0_8918, state));
        Assert.Equal(uint.MaxValue, state.D[0]);
    }

    [Fact]
    public void RawKeyConvertPreservesNullA2ForTheCurrentGuestKeyMap()
    {
        const uint keyMapLibrary = 0x3700, defaultKeyMap = 0x3D00;
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); InitializeLibrary(bus, keyMapLibrary, 0x3780, "keymap.library"); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        var calls = new List<(uint Entry, uint Continuation)>();
        using var console = new ConsoleDeviceServices(new CopperStartConsoleContext(bus, memory, input, _ => { }, (_, _, _, _) => { }, (_, entry, continuation) => calls.Add((entry, continuation))));
        Assert.True(console.TryInstall(ExecBase));

        // A device default exists, but public RawKeyConvert with a null A2
        // must leave keymap selection to native keymap.library's current map.
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0;
        bus.WriteLong(Request + 0x28, Window); Assert.True(Invoke(bus, Device - 6, state));
        bus.WriteWord(Request + 0x1C, 12); bus.WriteLong(Request + 0x28, defaultKeyMap); bus.WriteLong(Request + 0x24, 32);
        Assert.True(Invoke(bus, Device - 30, state));

        state.A[0] = Event; state.A[1] = Data; state.A[2] = 0; state.D[1] = 4;
        bus.WriteByte(Event + 4, 1, 10); bus.WriteWord(Event + 6, 0x20, 10);
        Assert.True(Invoke(bus, Device - 48, state));
        Assert.Equal(0u, state.A[2]);
        Assert.Equal(new[] { (keyMapLibrary - 36u, 0x00F0_8918u) }, calls);
    }

    [Fact]
    public void ConsoleKeyMapCommandsRoundTripUnitAndDeviceDefaultStructures()
    {
        const uint secondRequest = 0x3C00, firstMap = 0x3D00, defaultMap = 0x3E00, result = 0x3F00;
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, _, _) => { });
        Assert.True(console.TryInstall(ExecBase));
        for (var index = 0; index < 32; index++) { bus.WriteByte(firstMap + (uint)index, (byte)(0x40 + index), 10); bus.WriteByte(defaultMap + (uint)index, (byte)(0x80 + index), 10); }

        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0; bus.WriteLong(Request + 0x28, Window);
        Assert.True(Invoke(bus, Device - 6, state));
        void Command(uint request, ushort command, uint data)
        {
            bus.WriteWord(request + 0x1C, command); bus.WriteLong(request + 0x28, data); bus.WriteLong(request + 0x24, 32); state.A[1] = request;
            Assert.True(Invoke(bus, Device - 30, state)); Assert.Equal((byte)0, bus.ReadByte(request + 0x1F)); Assert.Equal(32u, bus.ReadLong(request + 0x20));
        }

        Command(Request, 10, firstMap); // CD_SETKEYMAP
        Command(Request, 9, result); // CD_ASKKEYMAP
        Assert.Equal(Enumerable.Range(0, 32).Select(index => (byte)(0x40 + index)), Enumerable.Range(0, 32).Select(index => bus.ReadByte(result + (uint)index)));

        Command(Request, 12, defaultMap); // CD_SETDEFAULTKEYMAP
        Command(Request, 11, result); // CD_ASKDEFAULTKEYMAP
        Assert.Equal(Enumerable.Range(0, 32).Select(index => (byte)(0x80 + index)), Enumerable.Range(0, 32).Select(index => bus.ReadByte(result + (uint)index)));

        // Newly opened units inherit the device default rather than another
        // unit's private keymap.
        bus.WriteLong(secondRequest + 0x28, Window); state.A[1] = secondRequest; state.D[0] = 0;
        Assert.True(Invoke(bus, Device - 6, state));
        Command(secondRequest, 9, result);
        Assert.Equal(Enumerable.Range(0, 32).Select(index => (byte)(0x80 + index)), Enumerable.Range(0, 32).Select(index => bus.ReadByte(result + (uint)index)));
    }

    [Fact]
    public void CopperStartDosConHandleUsesTheSharedConsoleSession()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        uint next = 0x5000;
        var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, _, _) => { });
        var dos = new DosServices(new CopperStartDosContext(
            new HostGuestMemory(bus),
            0,
            _ => "CON:0/0/320/100/Test",
            _ => null,
            _ => null,
            (_, _) => { },
            (bytes, flags) => memory.Allocate(bytes, flags),
            (address, length) => System.Text.Encoding.Latin1.GetString(Enumerable.Range(0, length).TakeWhile(index => bus.IsMappedMemoryRange(address + (uint)index, 1)).Select(index => bus.ReadByte(address + (uint)index)).ToArray()),
            (_, _) => { }));
        dos.AttachConsole(console);

        var state = new M68kCpuState { Cycles = 10 };
        state.D[1] = 0x4000;
        dos.Open(state);
        var handle = state.D[0];
        Assert.NotEqual(0u, handle);

        bus.WriteByte(Data, (byte)'O', 10);
        bus.WriteByte(Data + 1, (byte)'K', 10);
        state.D[1] = handle;
        state.D[2] = Data;
        state.D[3] = 2;
        dos.Write(state);
        Assert.Equal(2u, state.D[0]);

        state.D[1] = handle;
        dos.Close(state);
        Assert.Equal(0u, state.D[0]);
    }

    private static void InitializeDevice(AmigaBus bus)
    {
        bus.WriteLong(ExecBase + 0x15E, Device); bus.WriteLong(ExecBase + 0x162, 0); bus.WriteLong(ExecBase + 0x166, Device);
        bus.WriteLong(Device, ExecBase + 0x162); bus.WriteLong(Device + 4, ExecBase + 0x15E); bus.WriteLong(Device + 0x0A, Name);
        foreach (var (index, value) in "console.device\0".Select((value, index) => (index, value))) bus.WriteByte(Name + (uint)index, (byte)value, 0);
    }

    private static void InitializeLibrary(AmigaBus bus, uint library, uint name, string text)
    {
        bus.WriteLong(ExecBase + 0x17A, library); bus.WriteLong(ExecBase + 0x17E, 0); bus.WriteLong(ExecBase + 0x182, library);
        bus.WriteLong(library, ExecBase + 0x17E); bus.WriteLong(library + 4, ExecBase + 0x17A); bus.WriteLong(library + 0x0A, name);
        foreach (var (index, value) in (text + '\0').Select((value, index) => (index, value))) bus.WriteByte(name + (uint)index, (byte)value, 0);
    }

    private static bool Invoke(AmigaBus bus, uint address, M68kCpuState state)
        => bus.ReadWord(address) == 0xFF00 && bus.TryInvokeHostGateway(address, bus.ReadLong(address + 2), state);
}
