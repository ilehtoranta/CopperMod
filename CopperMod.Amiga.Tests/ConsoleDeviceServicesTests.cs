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
    public void ConsoleClearUsesNativeGraphicsAndCompletesOnlyAtItsContinuation()
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
        bus.WriteWord(Request + 0x1C, 5); bus.WriteByte(Request + 0x1E, 0, 10);

        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal(new[] { (graphics - 0xEAu, 0x00F0_8930u) }, calls);
        Assert.Empty(replies);
        Assert.True(Invoke(bus, 0x00F0_8930, state));
        Assert.Equal(new[] { Request }, replies);
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

        Assert.True(Invoke(bus, 0x00F0_8920, state));
        Assert.Equal(new[] { Request }, replies);
        Assert.True(Invoke(bus, 0x00F0_8920, state));
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
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 0;
        bus.WriteLong(Request + 0x28, Window); Assert.True(Invoke(bus, Device - 6, state));
        bus.WriteByte(Data, (byte)'A', 10); bus.WriteWord(Request + 0x1C, 3); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, 1); Assert.True(Invoke(bus, Device - 30, state));
        draws.Clear();
        bus.WriteWord(Window + 0x08, 160);

        console.ProcessPending(state);

        Assert.Equal(new[] { "A" }, draws);
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
        bus.WriteLong(Request + 0x24, (uint)edit.Length);

        Assert.True(Invoke(bus, Device - 30, state));
        Assert.Equal(new[] { "A" }, draws);
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
    public void ConsoleOpenEnforcesWindowAndUnitRules()
    {
        const uint noWindowRequest = 0x3D00, invalidUnitRequest = 0x3E00;
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        InitializeDevice(bus); bus.WriteLong(Window + 0x32, RastPort);
        uint next = 0x5000; var memory = new ExecMemoryOperations((bytes, _) => { var result = next; next += (uint)bytes; return result; }, (_, _) => { }, (address, bytes) => bus.ClearMemory(address, bytes));
        using var input = new InputDeviceServices(bus, _ => { }, (_, _, _) => { }, (_, _, _) => { });
        using var console = new ConsoleDeviceServices(bus, memory, input, _ => { }, (_, _, _, _) => { });
        Assert.True(console.TryInstall(ExecBase));
        var state = new M68kCpuState { Cycles = 10 };

        state.A[1] = Request; state.D[0] = uint.MaxValue; bus.WriteLong(Request + 0x28, 0); Assert.True(Invoke(bus, Device - 6, state)); Assert.Equal(0u, state.D[0]);
        state.D[0] = uint.MaxValue; Assert.True(Invoke(bus, Device - 6, state)); Assert.Equal(0xFFu, state.D[0]);
        state.A[1] = noWindowRequest; state.D[0] = 0; bus.WriteLong(noWindowRequest + 0x28, 0); Assert.True(Invoke(bus, Device - 6, state)); Assert.Equal(0xFFu, state.D[0]);
        state.A[1] = invalidUnitRequest; state.D[0] = 3; bus.WriteLong(invalidUnitRequest + 0x28, Window); Assert.True(Invoke(bus, Device - 6, state)); Assert.Equal(0xFFu, state.D[0]);
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
        var state = new M68kCpuState { Cycles = 10 }; state.A[1] = Request; state.D[0] = 2; bus.WriteLong(Request + 0x28, Window); Assert.True(Invoke(bus, Device - 6, state));
        bus.WriteByte(Event + 4, 1, 10); bus.WriteWord(Event + 6, 0x34); bus.WriteWord(Event + 8, 0x80); state.A[0] = Event; Assert.True(Invoke(bus, Device - 42, state)); console.ProcessPending(state);
        bus.WriteWord(Request + 0x1C, 2); bus.WriteLong(Request + 0x28, Data); bus.WriteLong(Request + 0x24, 8); Assert.True(Invoke(bus, Device - 30, state));

        Assert.Equal("\u009B0 v", System.Text.Encoding.Latin1.GetString(Enumerable.Range(0, 4).Select(index => bus.ReadByte(Data + (uint)index)).ToArray()));
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
    public void RawKeyConvertEntersTheCurrentGuestKeyMapAndReturnsItsBoundedResult()
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
        Assert.Equal(4u, state.D[0]);
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
