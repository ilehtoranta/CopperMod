using Copper68k;
using CopperMod.Amiga;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CopperStart.Exec;

namespace CopperMod.Amiga.Tests;

public sealed class ExecRegistryServicesTests
{
    private const uint ExecBase = 0x3000;
    private const uint CurrentTask = 0x2000;
    private const uint PortList = ExecBase + 0x188;
    private const uint SemaphoreList = ExecBase + 0x214;
    private const uint SalType = 0x8000_03E9;
    private const uint SalPriority = 0x8000_03EA;
    private const uint SalName = 0x8000_03EB;

    [Fact]
    public void AddExecNodeAAddsUniqueConfiguredNodesAndConstructsDocumentedClasses()
    {
        var fixture = new RegistryFixture();
        WriteString(fixture.Bus, 0x3800, "public-port");
        WriteString(fixture.Bus, 0x3820, "allocated-port");
        WriteString(fixture.Bus, 0x3840, "public-semaphore");

        const uint suppliedPort = 0x4000, suppliedTags = 0x4100;
        fixture.Bus.WriteByte(suppliedPort + 8, 4, 0);
        WriteTags(fixture.Bus, suppliedTags, (SalPriority, 12), (SalName, 0x3800));
        var supplied = new M68kCpuState();
        supplied.A[0] = suppliedPort; supplied.A[1] = suppliedTags;
        Assert.Equal(suppliedPort, fixture.Registry.AddExecNodeA(supplied));
        Assert.Equal(4, fixture.Bus.ReadByte(suppliedPort + 8));
        Assert.Equal(12, unchecked((sbyte)fixture.Bus.ReadByte(suppliedPort + 9)));
        Assert.Equal(0x3800u, fixture.Bus.ReadLong(suppliedPort + 10));
        Assert.Equal(suppliedPort, fixture.Bus.ReadLong(PortList));
        Assert.Equal(suppliedPort + 0x18, fixture.Bus.ReadLong(suppliedPort + 0x14));

        var duplicate = new M68kCpuState();
        duplicate.A[0] = 0x4200; duplicate.A[1] = suppliedTags;
        fixture.Bus.WriteByte(0x4200 + 8, 4, 0);
        Assert.Equal(0u, fixture.Registry.AddExecNodeA(duplicate));
        Assert.Equal(suppliedPort, fixture.Bus.ReadLong(PortList));

        var findPort = new M68kCpuState();
        findPort.D[0] = 5; // EXECLIST_PORT, not NT_MSGPORT.
        findPort.A[0] = 0x3800;
        Assert.Equal(suppliedPort, fixture.Registry.FindExecNode(findPort));

        const uint allocatedPortTags = 0x4300;
        WriteTags(fixture.Bus, allocatedPortTags, (SalType, 4), (SalName, 0x3820));
        var allocatedPortState = new M68kCpuState();
        allocatedPortState.A[1] = allocatedPortTags;
        var allocatedPort = fixture.Registry.AddExecNodeA(allocatedPortState);
        Assert.NotEqual(0u, allocatedPort);
        Assert.Equal(4, fixture.Bus.ReadByte(allocatedPort + 8));
        Assert.Equal(CurrentTask, fixture.Bus.ReadLong(allocatedPort + 0x10));
        var signalBit = fixture.Bus.ReadByte(allocatedPort + 0x0F);
        Assert.InRange(signalBit, (byte)0, (byte)31);
        Assert.NotEqual(0u, fixture.Bus.ReadLong(CurrentTask + 0x12) & (1u << signalBit));

        const uint allocatedSemaphoreTags = 0x4400;
        WriteTags(fixture.Bus, allocatedSemaphoreTags, (SalType, 15), (SalPriority, unchecked((uint)-4)), (SalName, 0x3840));
        var allocatedSemaphoreState = new M68kCpuState();
        allocatedSemaphoreState.A[1] = allocatedSemaphoreTags;
        var allocatedSemaphore = fixture.Registry.AddExecNodeA(allocatedSemaphoreState);
        Assert.NotEqual(0u, allocatedSemaphore);
        Assert.Equal(15, fixture.Bus.ReadByte(allocatedSemaphore + 8));
        Assert.Equal(-4, unchecked((sbyte)fixture.Bus.ReadByte(allocatedSemaphore + 9)));
        Assert.Equal(allocatedSemaphore, fixture.Bus.ReadLong(SemaphoreList));

        var findSemaphore = new M68kCpuState();
        findSemaphore.D[0] = 7; // EXECLIST_SEMAPHORE.
        findSemaphore.A[0] = 0x3840;
        Assert.Equal(allocatedSemaphore, fixture.Registry.FindExecNode(findSemaphore));
    }

    private static void WriteTags(AmigaBus bus, uint address, params (uint Tag, uint Data)[] values)
    {
        for (var index = 0; index < values.Length; index++)
        {
            bus.WriteLong(address + (uint)(index * 8), values[index].Tag);
            bus.WriteLong(address + (uint)(index * 8 + 4), values[index].Data);
        }
        bus.WriteLong(address + (uint)(values.Length * 8), 0);
        bus.WriteLong(address + (uint)(values.Length * 8 + 4), 0);
    }

    private static void WriteString(AmigaBus bus, uint address, string value)
    {
        for (var index = 0; index < value.Length; index++) bus.WriteByte(address + (uint)index, (byte)value[index], 0);
        bus.WriteByte(address + (uint)value.Length, 0, 0);
    }

    private sealed class RegistryFixture
    {
        private uint _nextAllocation = 0x5000;

        public RegistryFixture()
        {
            Bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
            var memory = new HostGuestMemory(Bus);
            var operations = new ExecMemoryOperations(Allocate, (_, _) => { }, (address, bytes) => Bus.ClearMemory(address, bytes));
            var context = new CopperStartExecContext(
                memory, () => ExecBase, () => CurrentTask, ReadString,
                (_, _, _) => { }, () => { }, _ => false, 0, _ => true,
                (_, _) => { }, _ => { }, (_, _, _) => { }, () => true, () => false,
                (_, _) => 0, operations, () => 0);
            var lists = new ExecListServices(memory, ReadString);
            lists.Initialize(PortList);
            lists.Initialize(SemaphoreList);
            lists.Initialize(ExecBase + 0x142);
            lists.Initialize(ExecBase + 0x150);
            lists.Initialize(ExecBase + 0x15E);
            lists.Initialize(ExecBase + 0x17A);
            lists.Initialize(ExecBase + 0x196);
            lists.Initialize(ExecBase + 0x1A4);
            var signals = new ExecSignalServices(context, () => true);
            Registry = new ExecRegistryServices(context, lists, signals, new ExecSemaphoreServices(context, signals));
        }

        public AmigaBus Bus { get; }
        public ExecRegistryServices Registry { get; }

        private uint Allocate(int bytes, uint flags)
        {
            var result = _nextAllocation;
            _nextAllocation += (uint)((bytes + 7) & ~7);
            Bus.ClearMemory(result, bytes);
            return result;
        }

        private string ReadString(uint address, int maximum)
        {
            var chars = new List<char>();
            for (var index = 0; index < maximum; index++)
            {
                var value = Bus.ReadByte(address + (uint)index);
                if (value == 0) break;
                chars.Add((char)value);
            }
            return new string(chars.ToArray());
        }
    }
}
