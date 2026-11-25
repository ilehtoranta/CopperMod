using Amiga;
using Copper68k;
using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed partial class KickstartRomLayersDifferentialTests
{
    private static byte[] ReadBytes(AmigaBus bus, uint start, int count)
    {
        var bytes = new byte[count];
        for (var index = 0; index < bytes.Length; index++) bytes[index] = bus.ReadByte(start + (uint)index);
        return bytes;
    }

    private static OracleContext CreateNativeIntuitionOracle(byte[] rom, bool suppressInputDeviceOverlay,
        bool ntsc = false, bool ecs = false, int chipRamBytes = 512 * 1024)
    {
        var chipset = ecs ? (ntsc ? AmigaChipset.EcsNtsc : AmigaChipset.EcsPal)
                          : (ntsc ? AmigaChipset.OcsNtsc : AmigaChipset.OcsPal);
        var machine = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot)
            .WithCpu(AmigaM68kCoreFactory.Default, M68kBackendKind.AccurateM68000)
            .WithChipset(chipset).WithChipRam(chipRamBytes)
            .WithKickstart(KickstartConfiguration.FromRomImage(KickstartVersion.Kickstart31, rom))
            .WithLiveAgnusDma(true));
        try
        {
            var boot = new AmigaBootController(machine);
            if (suppressInputDeviceOverlay) boot.SuppressCopperStartInputDeviceOverlayForTest();
            boot.StartKickstartRomBoot();
            for (var chunk = 0; chunk < 160; chunk++)
            {
                boot.ContinueExecution(250_000);
                var exec = machine.Bus.ReadLong(4);
                var graphics = FindLibrary(machine.Bus, exec, "graphics.library");
                var layers = FindLibrary(machine.Bus, exec, "layers.library");
                if (graphics != 0 && layers != 0)
                    return new OracleContext(machine, boot, exec, graphics, layers, true);
                if (machine.Cpu.State.Halted) break;
            }
            throw new InvalidOperationException($"Kickstart did not publish graphics.library and layers.library. PC=${machine.Cpu.State.ProgramCounter:X8}.");
        }
        catch
        {
            machine.Dispose();
            throw;
        }
    }

    private static uint ContinueUntilNativeIntuitionPublished(OracleContext context)
    {
        for (var chunk = 0; chunk <= 160; chunk++)
        {
            var intuition = FindLibrary(context.Bus, context.ExecBase, "intuition.library");
            if (intuition != 0) return intuition;
            if (context.Machine.Cpu.State.Halted || chunk == 160) break;
            context.Boot.ContinueExecution(250_000);
        }
        throw new InvalidOperationException($"Kickstart did not publish intuition.library. PC=${context.Machine.Cpu.State.ProgramCounter:X8}.");
    }

    private void WriteIntuitionMachineState(string name, OracleContext context)
        => _output.WriteLine($"{name}: PC=${context.Machine.Cpu.State.ProgramCounter:X8}, ExecBase=${context.ExecBase:X8}, GfxBase=${context.GraphicsBase:X8}, LayersBase=${context.LayersBase:X8}");

    private static M68kCpuState RunNativeTask(OracleContext context, uint entry, uint marker)
    {
        // Register a real task with the ROM scheduler. SetPrefs and OpenScreen
        // can sleep and must resume with the task's own signal bits and stack.
        var task = context.Allocate(global::Amiga.Task.Size);
        const uint stackSize = 16384;
        var stack = context.Allocate(stackSize);
        var bus = context.Bus;
        bus.WriteHostByte(task + ExecLayout.Node.Type, (byte)NodeType.Task);
        bus.WriteHostByte(task + ExecLayout.Node.Priority, 1);
        bus.WriteLong(task + ExecLayout.Task.StackLower, stack);
        bus.WriteLong(task + ExecLayout.Task.StackUpper, stack + stackSize);
        bus.WriteLong(task + ExecLayout.Task.StackPointer, stack + stackSize);
        var list = task + ExecLayout.Task.MemoryEntries;
        bus.WriteLong(list, list + 4);
        bus.WriteLong(list + 8, list);
        bool Finished(M68kCpuState state) => state.D[7] == marker &&
            bus.ReadLong(context.ExecBase + ExecLayout.ExecBase.ThisTask) == task;
        var (state, _) = context.InvokeCore(context.ExecBase, ExecLvo.AddTask, registers =>
        {
            registers.A[1] = task;
            registers.A[2] = entry;
            registers.A[3] = 0;
        }, MaximumVectorInstructions, Finished);
        for (var chunk = 0; !Finished(state) && !state.Halted && chunk < 160; chunk++)
            context.Boot.ContinueExecution(250_000);
        Assert.True(Finished(state), $"Guest task ${task:X8} stopped at ${state.ProgramCounter:X8} before writing ${marker:X8}.");
        return state;
    }
}
