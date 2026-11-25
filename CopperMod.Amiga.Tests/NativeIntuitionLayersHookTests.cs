using Amiga;
using CopperMod.Amiga.CopperStart.Layers;

namespace CopperMod.Amiga.Tests;

public sealed partial class CopperStartLayersBootTests
{
    // Exercise the resident's actual 68000 entries while a Layers hook owns its
    // return packet. Exec and Layers use the ordinary booted host gateways.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeIntuitionCallsInsideLayersHookReturnToTheirOriginalTask(bool split)
    {
        using var machine = CreateMachine();
        var boot = new AmigaBootController(machine);
        boot.StartBootFromDisk(CreateBootableDisk());
        var bus = machine.Bus;
        var actor = LayersGuestFixture.CurrentTask(bus);
        var layers = boot.CopperStartLayersLibraryBase;
        var info = NewLayerInfo(bus, layers);
        var surface = CreateLayer(bus, layers, info, 0, 0, 15, 15);
        var cover = split ? CreateLayerOnBitMaps(bus, layers, info, surface.BitMap, 0,
            LayerCreationFlags.Simple, 6, 0, 9, 15) : default;
        using var resident = new NativeIntuitionTestResident(bus);
        var ibase = resident.Library;
        var exec = bus.ReadLong(4);
        var user = LayersGuestFixture.GuestStack.Allocate(bus);
        var supervisor = LayersGuestFixture.GuestStack.Allocate(bus);
        LayersGuestFixture.SetTaskStack(bus, actor, user);
        var callerCode = AllocateGuestMemory(bus, 256);
        var hookCode = AllocateGuestMemory(bus, 256);
        var hook = LayersGuestFixture.Hook(bus, hookCode);
        var caller = new InstructionWriter(bus, callerCode, 256);
        caller.MoveAddressImmediate(0, hook.Raw);
        caller.MoveAddressImmediate(1, surface.RastPort);
        caller.MoveAddressImmediate(2, 0);
        caller.MoveAddressImmediate(6, layers);
        var done = caller.JumpSubroutine(Lvo(layers, LayersLvo.DoHookClipRects));
        caller.BranchToSelf();
        var callback = new InstructionWriter(bus, hookCode, 256);
        callback.MoveAddressImmediate(6, ibase);
        var locked = callback.JumpSubroutine(resident.Entry(-414));
        var view = callback.JumpSubroutine(resident.Entry(-294));
        var released = callback.JumpSubroutine(resident.Entry(-420));
        callback.Return();
        LayersGuestFixture.StartRequester(machine, callerCode, user, supervisor);

        var claims = new HashSet<uint>();
        byte nesting = 0;
        int opened = 0, views = 0, closed = 0;
        var state = machine.Cpu.State;
        var until = state.Cycles + 2_000_000;
        for (int step = 0; state.ProgramCounter != done && step < 20000; step++)
        {
            Assert.False(state.Halted);
            Assert.Equal(actor, LayersGuestFixture.CurrentTask(bus));
            if (state.ProgramCounter == hookCode)
            {
                Assert.Equal(hook.Raw, state.A[0]);
                Assert.True(boot.CopperStartLayersHasPendingCallbackForTest);
                Assert.True(claims.Add(boot.CopperStartLayersPendingCallbackNonceForTest));
                nesting = bus.ReadByte(exec + ExecLayout.ExecBase.TaskDisableNestCount);
            }
            if (state.ProgramCounter == locked)
            {
                Assert.Equal(unchecked((byte)(nesting + 1)), bus.ReadByte(exec + ExecLayout.ExecBase.TaskDisableNestCount));
                Assert.Equal(0u, state.D[0]);
                opened++;
            }
            if (state.ProgramCounter == view) { Assert.Equal(ibase + 34, state.D[0]); views++; }
            if (state.ProgramCounter == released)
            {
                Assert.Equal(nesting, bus.ReadByte(exec + ExecLayout.ExecBase.TaskDisableNestCount));
                closed++;
            }
            Assert.Equal(1, boot.ContinueCopperStartRuntimeUntilCycle(until, maxInstructions: 1).InstructionsExecuted);
        }
        Assert.Equal(done, state.ProgramCounter);
        Assert.Equal(split ? 2 : 1, claims.Count);
        Assert.Equal(claims.Count, opened);
        Assert.Equal(opened, views);
        Assert.Equal(opened, closed);
        Assert.Equal(user.Upper, state.A[7]);
        Assert.False(boot.CopperStartLayersHasPendingCallbackForTest);
        Assert.Equal(LayersCallbackReturnFault.None, boot.CopperStartLayersLastCallbackReturnFaultForTest);
        if (split) DeleteLayer(bus, layers, cover.Layer);
        DeleteLayer(bus, layers, surface.Layer);
        FreeBitMap(bus, surface.BitMap);
        DisposeLayerInfo(bus, layers, info);
    }
}
