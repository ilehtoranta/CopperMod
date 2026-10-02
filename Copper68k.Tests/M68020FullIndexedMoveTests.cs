using Copper68k;
using static Copper68k.Tests.M68kInterpreterTestHelpers;

namespace Copper68k.Tests;

public sealed class M68020FullIndexedMoveTests
{
    private const uint Code = 0xF80000;

    public static IEnumerable<object[]> JumpModes()
    {
        foreach (var mode in Modes())
        foreach (var subroutine in new[] { false, true })
            yield return [mode[0], mode[1], mode[2], subroutine];
    }

    [Theory]
    [MemberData(nameof(JumpModes))]
    public void FullJumpResolvesTargetAndSubroutineReturnBeforeChangingStack(int bd, int indirect, bool suppressIndex, bool subroutine)
    {
        var bus = new RecordingBus();
        var words = new List<ushort> { (ushort)(subroutine ? 0x4EB4 : 0x4EF4),
            (ushort)(0x1100 | bd << 4 | indirect | (suppressIndex ? 0x40 : 0)) };
        if (bd == 2) words.Add(0xFFFC);
        if (bd == 3) words.AddRange([0xFFFF, 0xFFF8]);
        var outer = (indirect & 3) >= 2 ? -16 : 0;
        if ((indirect & 3) == 2) words.Add(0xFFF0);
        if ((indirect & 3) == 3) words.AddRange([0xFFFF, 0xFFF0]);
        WriteWords(bus.Memory, Code, words.ToArray());
        var address = bd == 1 ? 0x2000u : bd == 2 ? 0x1FFCu : 0x1FF8u;
        var index = suppressIndex ? 0u : 4u;
        var target = address + index;
        if (indirect != 0)
        {
            bus.Memory.WriteLong(address + (indirect < 4 ? index : 0), 0x3000);
            target = unchecked((uint)(0x3000 + outer)) + (indirect >= 5 ? index : 0);
        }
        WriteWords(bus.Memory, target, 0x4E71);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus); cpu.Reset(Code, 0x5000);
        cpu.State.A[4] = 0x2000; cpu.State.D[1] = 4; cpu.State.StatusRegister = 0x201F;
        bus.Reads.Clear(); cpu.ExecuteInstruction();
        Assert.Equal(target, cpu.State.ProgramCounter); Assert.Equal(0x201F, cpu.State.StatusRegister);
        Assert.Equal(0x2000u, cpu.State.A[4]); Assert.Equal(subroutine ? 0x4FFCu : 0x5000u, cpu.State.A[7]);
        if (subroutine) Assert.Equal(Code + (uint)words.Count * 2, bus.Memory.ReadLong(0x4FFC));
        Assert.DoesNotContain(target, bus.Reads);
        cpu.ExecuteInstruction(); Assert.Equal(target + 2, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x4EB7, 0x0151, 0x5000u, 0x3000u)]
    [InlineData(0x4EB0, 0xF915, 0x2000u, 0x8000u)]
    public void FullSubroutineLatchesStackBaseAndIndexBeforePush(ushort opcode, ushort extension, uint pointerAddress, uint target)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, Code, opcode, extension);
        bus.WriteLong(pointerAddress, 0x3000);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus); cpu.Reset(Code, 0x5000);
        cpu.State.A[0] = 0x2000; cpu.ExecuteInstruction();
        Assert.Equal(target, cpu.State.ProgramCounter); Assert.Equal(Code + 4, bus.ReadLong(0x4FFC));
        Assert.Equal(0x4FFCu, cpu.State.A[7]); Assert.Equal(0x4FFCu, cpu.State.InterruptStackPointer);
    }

    [Theory]
    [InlineData(0x4EFB, false)] [InlineData(0x4EBB, true)]
    public void FullPcJumpUsesExtensionWordBaseAndCompleteReturnAddress(ushort opcode, bool subroutine)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, Code, opcode, 0x0161, 6);
        bus.WriteLong(Code + 8, 0x3000);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus); cpu.Reset(Code, 0x5000);
        cpu.State.StatusRegister = 0x201F; cpu.ExecuteInstruction();
        Assert.Equal(0x3000u, cpu.State.ProgramCounter); Assert.Equal(0x201F, cpu.State.StatusRegister);
        if (subroutine) Assert.Equal(Code + 6, bus.ReadLong(0x4FFC));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void FullLeaStoresCalculatedAddressWithoutReadingFinalOperand(int bd, int indirect, bool suppressIndex)
    {
        var bus = new RecordingBus();
        var words = new List<ushort> { 0x49F4, (ushort)(0x1100 | bd << 4 | indirect | (suppressIndex ? 0x40 : 0)) };
        if (bd == 2) words.Add(0xFFFC);
        if (bd == 3) words.AddRange([0xFFFF, 0xFFF8]);
        var outer = (indirect & 3) >= 2 ? -16 : 0;
        if ((indirect & 3) == 2) words.Add(0xFFF0);
        if ((indirect & 3) == 3) words.AddRange([0xFFFF, 0xFFF0]);
        WriteWords(bus.Memory, Code, words.ToArray());
        var address = bd == 1 ? 0x2000u : bd == 2 ? 0x1FFCu : 0x1FF8u;
        var index = suppressIndex ? 0u : 4u;
        var target = address + index;
        if (indirect != 0)
        {
            bus.Memory.WriteLong(address + (indirect < 4 ? index : 0), 0x3000);
            target = unchecked((uint)(0x3000 + outer)) + (indirect >= 5 ? index : 0);
        }
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus); cpu.Reset(Code, 0x5000);
        cpu.State.A[4] = 0x2000; cpu.State.D[1] = 4; cpu.State.StatusRegister = 0x201F;
        bus.Reads.Clear(); cpu.ExecuteInstruction();
        Assert.Equal(target, cpu.State.A[4]); Assert.Equal(4u, cpu.State.D[1]);
        Assert.Equal(0x201F, cpu.State.StatusRegister); Assert.Equal(Code + (uint)words.Count * 2, cpu.State.ProgramCounter);
        Assert.DoesNotContain(target, bus.Reads);
        if (indirect == 0) Assert.Empty(bus.Reads);
        Assert.Equal(8 + (bd == 2 ? 2 : bd == 3 ? 6 : 0) +
            (indirect == 0 ? 0 : 5 + ((indirect & 3) >= 2 ? 2 : 0)), cpu.State.NativeCycles);
    }

    [Theory]
    [InlineData(0x41FB)] [InlineData(0x4FFB)]
    public void FullPcLeaUsesExtensionWordBaseAndMaintainsActiveStack(ushort opcode)
    {
        var bus = new RecordingBus(); WriteWords(bus.Memory, Code, opcode, 0x0161, 6);
        bus.Memory.WriteLong(Code + 8, 0x3000);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus); cpu.Reset(Code, 0x5000);
        cpu.State.StatusRegister = 0x201F; bus.Reads.Clear(); cpu.ExecuteInstruction();
        Assert.Equal(0x3000u, cpu.State.A[(opcode >> 9) & 7]); Assert.Equal(Code + 6, cpu.State.ProgramCounter);
        Assert.Equal(0x201F, cpu.State.StatusRegister); Assert.DoesNotContain(0x3000u, bus.Reads);
        if (((opcode >> 9) & 7) == 7) Assert.Equal(0x3000u, cpu.State.InterruptStackPointer);
    }

    [Theory]
    [InlineData(M68kCpuModel.M68020)] [InlineData(M68kCpuModel.M68EC020)]
    [InlineData(M68kCpuModel.M68030)] [InlineData(M68kCpuModel.M68040)] [InlineData(M68kCpuModel.M68060)]
    public void FullIndexedRegisterReadAndStorePreserveSharedAdvancedProfileSemantics(M68kCpuModel model)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, Code, 0x2034, 0x0151, 0x2B80, 0x0151, 0x4E71);
        bus.WriteLong(0x2000, 0x3000); bus.WriteLong(0x3000, 0x12345678); bus.WriteLong(0x4000, 0x5000);
        using var cpu = M68kCoreFactory.Default.Create(model, bus); cpu.Reset(Code, 0x6000);
        cpu.State.A[4] = 0x2000; cpu.State.A[5] = 0x4000; cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction(); Assert.Equal(0x12345678u, cpu.State.D[0]); Assert.Equal(Code + 4, cpu.State.ProgramCounter);
        cpu.ExecuteInstruction(); Assert.Equal(0x12345678u, bus.ReadLong(0x5000));
        Assert.Equal(Code + 8, cpu.State.ProgramCounter); Assert.Equal(0x10, cpu.State.StatusRegister & 31);
        cpu.ExecuteInstruction(); Assert.Equal(Code + 10, cpu.State.ProgramCounter);
    }

    [Fact]
    public void MemoryIndirectPointerReadPrecedesFinalOperandReadAndLatchesIndex()
    {
        var bus = new RecordingBus(); WriteWords(bus.Memory, Code, 0x2874, 0xC911, 0x4E71);
        bus.Memory.WriteLong(0x4000, 0x3000); bus.Memory.WriteLong(0x3000, 0x89ABCDEF);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus); cpu.Reset(Code, 0x5000);
        cpu.State.A[4] = 0x2000; bus.Reads.Clear(); cpu.ExecuteInstruction();
        Assert.Equal(0x89ABCDEFu, cpu.State.A[4]);
        Assert.Equal(0x4000u, bus.Reads[0]);
        Assert.True(bus.Reads.IndexOf(0x3000) > bus.Reads.IndexOf(0x4000));
        Assert.All(bus.Reads, address => Assert.Contains(address, new[] { 0x4000u, 0x4002u, 0x3000u, 0x3002u }));
    }

    private sealed class RecordingBus : IM68kBus, IM68kCodeReader
    {
        public ZeroWaitCodeBus Memory { get; } = new();
        public List<uint> Reads { get; } = [];
        public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind kind)
        { if (kind == M68kBusAccessKind.CpuDataRead) Reads.Add(address); return Memory.ReadByte(address, ref cycle, kind); }
        public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind kind)
        { if (kind == M68kBusAccessKind.CpuDataRead) Reads.Add(address); return Memory.ReadWord(address, ref cycle, kind); }
        public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind kind)
        { if (kind == M68kBusAccessKind.CpuDataRead) Reads.Add(address); return Memory.ReadLong(address, ref cycle, kind); }
        public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind kind) => Memory.WriteByte(address, value, ref cycle, kind);
        public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind kind) => Memory.WriteWord(address, value, ref cycle, kind);
        public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind kind) => Memory.WriteLong(address, value, ref cycle, kind);
        public ushort ReadHostWord(uint address) => Memory.ReadWord(address);
        public void ResetExternalDevices(long cycle) => Memory.ResetExternalDevices(cycle);
    }

    public static IEnumerable<object[]> Modes()
    {
        foreach (var bd in new[] { 1, 2, 3 })
        foreach (var indirect in new[] { 0, 1, 2, 3, 5, 6, 7 })
            yield return [bd, indirect, false];
        foreach (var bd in new[] { 1, 2, 3 })
        foreach (var indirect in new[] { 0, 1, 2, 3 })
            yield return [bd, indirect, true];
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void FullDestinationConsumesDisplacementsAndSelectsPointer(int bd, int indirect, bool suppressIndex)
    {
        var bus = new ZeroWaitCodeBus();
        var words = new List<ushort> { 0x2980, (ushort)(0x1100 | bd << 4 | indirect | (suppressIndex ? 0x40 : 0)) };
        if (bd == 2) words.Add(0xFFFC);
        if (bd == 3) words.AddRange([0xFFFF, 0xFFF8]);
        var outer = (indirect & 3) >= 2 ? -16 : 0;
        if ((indirect & 3) == 2) words.Add(0xFFF0);
        if ((indirect & 3) == 3) words.AddRange([0xFFFF, 0xFFF0]);
        WriteWords(bus, Code, words.ToArray());
        var address = bd == 1 ? 0x2000u : bd == 2 ? 0x1FFCu : 0x1FF8u;
        var index = suppressIndex ? 0u : 4u;
        uint target;
        if (indirect == 0) target = address + index;
        else
        {
            var postIndexed = indirect >= 5;
            bus.WriteLong(address + (postIndexed ? 0u : index), 0x3000);
            target = unchecked((uint)(0x3000 + outer)) + (postIndexed ? index : 0);
        }
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(Code, 0x5000); cpu.State.A[4] = 0x2000; cpu.State.D[1] = 4; cpu.State.D[0] = 0x89ABCDEF;
        cpu.State.StatusRegister = 0x201F; cpu.ExecuteInstruction();
        Assert.Equal(0x89ABCDEFu, bus.ReadLong(target)); Assert.Equal(0x89ABCDEFu, cpu.State.D[0]);
        Assert.Equal(Code + (uint)words.Count * 2, cpu.State.ProgramCounter);
        Assert.Equal(0x18, cpu.State.StatusRegister & 31); Assert.Equal(0x2000u, cpu.State.A[4]);
        Assert.Equal(8 + (bd == 2 ? 2 : bd == 3 ? 6 : 0) +
            (indirect == 0 ? 0 : 4 + ((indirect & 3) == 3 ? 3 : (indirect & 3) == 2 ? 2 : 0)), cpu.State.NativeCycles);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void FullSourceConsumesDisplacementsAndSelectsPreOrPostIndex(int bd, int indirect, bool suppressIndex)
    {
        var bus = new ZeroWaitCodeBus();
        var words = new List<ushort> { 0x2034, (ushort)(0x1100 | bd << 4 | indirect | (suppressIndex ? 0x40 : 0)) };
        if (bd == 2) words.Add(0xFFFC);
        if (bd == 3) words.AddRange([0xFFFF, 0xFFF8]);
        var outer = (indirect & 3) >= 2 ? -16 : 0;
        if ((indirect & 3) == 2) words.Add(0xFFF0);
        if ((indirect & 3) == 3) words.AddRange([0xFFFF, 0xFFF0]);
        WriteWords(bus, Code, words.ToArray());
        WriteWords(bus, Code + (uint)words.Count * 2, 0x4E71);
        var address = bd == 1 ? 0x2000u : bd == 2 ? 0x1FFCu : 0x1FF8u;
        var index = suppressIndex ? 0u : 4u;
        uint valueAddress;
        if (indirect == 0) valueAddress = address + index;
        else
        {
            var postIndexed = indirect >= 5;
            bus.WriteLong(address + (postIndexed ? 0u : index), 0x3000);
            valueAddress = unchecked((uint)(0x3000 + outer)) + (postIndexed ? index : 0);
        }
        bus.WriteLong(valueAddress, 0x89ABCDEF);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(Code, 0x4000); cpu.State.A[4] = 0x2000; cpu.State.D[1] = 4; cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction();
        Assert.Equal(0x89ABCDEFu, cpu.State.D[0]); Assert.Equal(Code + (uint)words.Count * 2, cpu.State.ProgramCounter);
        Assert.Equal(0x18, cpu.State.StatusRegister & 31); Assert.Equal(0x2000u, cpu.State.A[4]);
        Assert.Equal(4u, cpu.State.D[1]);
        // MC68020UM 8.2.6 cache-case MOVE source -> Rn; elapsed bus time is separate.
        Assert.Equal(9 + (bd == 2 ? 2 : bd == 3 ? 6 : 0) +
            (indirect == 0 ? 0 : 5 + ((indirect & 3) >= 2 ? 2 : 0)), cpu.State.NativeCycles);
        // A following ordinary instruction must not inherit a full-EA cost.
        var before = cpu.State.NativeCycles;
        cpu.ExecuteInstruction(); Assert.InRange(cpu.State.NativeCycles - before, 4, 5);
    }

    [Theory]
    [InlineData(0x0130)] [InlineData(0x01B0)] [InlineData(0x0170)] [InlineData(0x01F0)]
    public void BaseAndIndexSuppressionAreIndependent(ushort extension)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, Code, 0x2034, extension, 0, 0x3000);
        var target = 0x3000u + ((extension & 0x80) == 0 ? 0x1000u : 0) + ((extension & 0x40) == 0 ? 0x20u : 0);
        bus.WriteLong(target, 0x12345678);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(Code, 0x5000); cpu.State.A[4] = 0x1000; cpu.State.D[0] = 0x20;
        cpu.ExecuteInstruction(); Assert.Equal(0x12345678u, cpu.State.D[0]); Assert.Equal(Code + 8, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(false, 0)] [InlineData(false, 1)] [InlineData(false, 2)] [InlineData(false, 3)]
    [InlineData(true, 0)] [InlineData(true, 1)] [InlineData(true, 2)] [InlineData(true, 3)]
    public void FullWordAndLongIndexesSignExtendBeforeScaling(bool longIndex, int scale)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, Code, 0x2034, (ushort)(0x1110 | (longIndex ? 0x800 : 0) | scale << 9));
        var index = longIndex ? -65538 : -2; var target = unchecked((uint)(0x100000 + index * (1 << scale)));
        bus.WriteLong(target, 0xFEDCBA98);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(Code, 0x5000); cpu.State.A[4] = 0x100000; cpu.State.D[1] = 0xFFFEFFFE;
        cpu.ExecuteInstruction(); Assert.Equal(0xFEDCBA98u, cpu.State.D[0]); Assert.Equal(0xFFFEFFFEu, cpu.State.D[1]);
    }

    [Theory]
    [InlineData(0x1034, 0xABCD1280u, 0x18)] [InlineData(0x3034, 0xABCD8001u, 0x18)]
    [InlineData(0x2034, 0x80012345u, 0x18)] [InlineData(0x3074, 0xFFFF8001u, 0x1F)]
    [InlineData(0x2074, 0x80012345u, 0x1F)]
    [InlineData(0x103B, 0xABCD1280u, 0x18)] [InlineData(0x303B, 0xABCD8001u, 0x18)]
    [InlineData(0x203B, 0x80012345u, 0x18)] [InlineData(0x307B, 0xFFFF8001u, 0x1F)]
    [InlineData(0x207B, 0x80012345u, 0x1F)]
    public void FullMoveWidthsAndPcBasesPreserveMoveaFlags(ushort opcode, uint expected, int flags)
    {
        var bus = new ZeroWaitCodeBus();
        var pc = (opcode & 7) == 3;
        WriteWords(bus, Code, opcode, 0x0151); // Null base displacement, index suppressed, indirect with null outer.
        bus.WriteLong(0x2000, 0x3000);
        if (pc) { WriteWords(bus, Code, opcode, 0x0161, 6); bus.WriteLong(Code + 8, 0x3000); }
        bus.WriteLong(0x3000, 0x80012345);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(Code, 0x5000); cpu.State.A[4] = 0x2000; cpu.State.D[0] = 0xABCD1234; cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, (opcode & 0x40) != 0 ? cpu.State.A[0] : cpu.State.D[0]);
        Assert.Equal(flags, cpu.State.StatusRegister & 31); Assert.Equal(Code + (pc ? 6u : 4u), cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x0100)] [InlineData(0x0118)] [InlineData(0x0114)]
    [InlineData(0x0154)] [InlineData(0x0155)] [InlineData(0x0156)] [InlineData(0x0157)]
    public void ReservedFullExtensionsStillStopExplicitly(ushort extension)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, Code, 0x2034, extension, 0x4E71);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus); cpu.Reset(Code, 0x5000);
        Assert.Throws<UnsupportedM68kTimingException>(() => cpu.ExecuteInstruction()); Assert.Equal(Code + 4, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x9039, 0xFFFF0000u, 0xFFFF00FFu, 0x19)]
    [InlineData(0x9079, 0xABCD8000u, 0xABCD7FFFu, 2)]
    [InlineData(0x90B9, 0u, 0xFFFFFFFFu, 0x19)]
    public void SubAbsoluteLongReadsSizedOperandAndUpdatesArithmeticFlags(ushort opcode, uint destination, uint expected, int flags)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, Code, opcode, 0, 0x3000);
        bus.WriteLong(0x3000, opcode == 0x9039 ? 0x01000000u : opcode == 0x9079 ? 0x00010000u : 1u);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(Code, 0x5000); cpu.State.D[0] = destination; cpu.State.StatusRegister = 0x201F; cpu.ExecuteInstruction();
        Assert.Equal(expected, cpu.State.D[0]); Assert.Equal(flags, cpu.State.StatusRegister & 31); Assert.Equal(Code + 6, cpu.State.ProgramCounter);
    }
}
