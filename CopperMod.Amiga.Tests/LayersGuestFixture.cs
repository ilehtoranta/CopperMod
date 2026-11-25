using Amiga;
using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

internal static class LayersGuestFixture
{
    internal static APTR Allocate(AmigaBus bus, uint bytes)
        => APTR.FromPointer(CopperStartLayersBootTests.AllocateGuestMemory(bus, bytes));

    internal static APTR Hook(AmigaBus bus, uint entry)
    {
        var address = Allocate(bus, global::Amiga.Hook.Size);
        var hook = new global::Amiga.Hook();
        hook.Initialize(APTR.FromPointer(entry));
        var memory = new LayersTestGuestMemory(bus);
        UtilityHookCodec.Write(ref memory, address, hook);
        return address;
    }

    internal readonly record struct GuestStack(uint Lower, uint Upper)
    {
        internal uint Storage => Lower - 4;
        internal uint Bytes => Upper - Lower + 8;
        internal void AssertIntact(AmigaBus bus)
        {
            Assert.Equal(0xDEADC0DEu, bus.ReadLong(Storage));
            Assert.Equal(0xCAFEF00Du, bus.ReadLong(Upper));
        }

        internal static GuestStack Allocate(AmigaBus bus)
        {
            const uint size = 8192;
            var storage = LayersGuestFixture.Allocate(bus, size + 8).Raw;
            bus.WriteLong(storage, 0xDEADC0DE);
            bus.WriteLong(storage + size + 4, 0xCAFEF00D);
            return new GuestStack(storage + 4, storage + size + 4);
        }
    }

    internal static APTR CurrentTask(AmigaBus bus) => APTR.FromPointer(bus.ReadLong(bus.ReadLong(4) + ExecLayout.ExecBase.ThisTask));

    internal static global::Amiga.Task ReadTask(AmigaBus bus, APTR task)
    {
        var memory = new LayersTestGuestMemory(bus);
        return ExecTaskCodec.Read(ref memory, task);
    }

    internal static void SetTaskStack(AmigaBus bus, uint actor, GuestStack stack)
    {
        Assert.NotEqual(0u, actor);
        bus.WriteLong(actor + ExecLayout.Task.StackLower, stack.Lower);
        bus.WriteLong(actor + ExecLayout.Task.StackUpper, stack.Upper);
        bus.WriteLong(actor + ExecLayout.Task.StackPointer, stack.Upper);
    }

    internal static void StartRequester(Machine machine, uint entry, GuestStack user, GuestStack supervisor)
    {
        var cpu = machine.Cpu.State;
        cpu.Stopped = false;
        cpu.Halted = false;
        cpu.ResetStackPointers(supervisor.Upper, user.Upper, supervisorMode: false);
        cpu.ProgramCounter = entry;
    }
}
