using Copper68k;

namespace Copper68k.Tests;

public sealed class M68020ContinuationTests
{
    public static IEnumerable<object[]> AbsoluteOrs()
    {
        foreach (var model in new[] { M68kCpuModel.M68020, M68kCpuModel.M68EC020, M68kCpuModel.M68030 })
        foreach (var size in new[] { 1, 2, 4 })
        foreach (var resultCase in new[] { 0, 1, 2 })
            yield return new object[] { model, size, resultCase };
    }

    [Theory]
    [MemberData(nameof(AbsoluteOrs))]
    public void OrToAbsoluteLongPreservesSurroundingMemoryAndExtend(M68kCpuModel model, int size, int resultCase)
    {
        var mask = size == 1 ? 0xFFu : size == 2 ? 0xFFFFu : uint.MaxValue;
        var initial = resultCase == 0 ? 0u : resultCase == 1 ? (mask >> 1) + 1 : 0x40u;
        var source = (~mask) | (resultCase == 0 ? 0u : 1u);
        var expected = resultCase == 0 ? 0u : resultCase == 1 ? (mask >> 1) + 2 : 0x41u;
        var shift = 32 - size * 8;
        var guard = size == 1 ? 0xABCDEFu : size == 2 ? 0xCDEFu : 0u;
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, (ushort)(0x8339 | ((size == 1 ? 0 : size == 2 ? 1 : 2) << 6)), 0x00C0, 0x4001);
        bus.WriteWord(0xC03FFF, 0xAAAA);
        bus.WriteLong(0xC04001, (initial << shift) | guard);
        bus.WriteWord(0xC04005, 0xBBBB);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.D[1] = source;
        cpu.State.StatusRegister = 0x271F;
        cpu.ExecuteInstruction();
        Assert.Equal((expected << shift) | guard, bus.ReadLong(0xC04001));
        Assert.Equal(0xAAAA, bus.ReadWord(0xC03FFF));
        Assert.Equal(0xBBBB, bus.ReadWord(0xC04005));
        Assert.Equal(source, cpu.State.D[1]);
        Assert.Equal(resultCase == 0 ? 0x2714 : resultCase == 1 ? 0x2718 : 0x2710, cpu.State.StatusRegister);
        Assert.Equal(0x1006u, cpu.State.ProgramCounter);
    }

    public static IEnumerable<object[]> AbsoluteByteCompares()
    {
        foreach (var model in new[] { M68kCpuModel.M68020, M68kCpuModel.M68EC020, M68kCpuModel.M68030 })
        foreach (var value in new byte[] { 0, 0x80, 1 })
            yield return new object[] { model, value };
    }

    [Theory]
    [MemberData(nameof(AbsoluteByteCompares))]
    public void CompareByteImmediateAbsoluteIgnoresUpperImmediateByteAndPreservesMemory(M68kCpuModel model, byte value)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, 0x0C39, 0xAB01, 0x00C0, 0x4001);
        var memory = 0xAA00CCDDu | ((uint)value << 16);
        bus.WriteLong(0xC04000, memory);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.StatusRegister = 0x271F;
        cpu.ExecuteInstruction();
        Assert.Equal(value == 0 ? 0x2719 : value == 0x80 ? 0x2712 : 0x2714, cpu.State.StatusRegister);
        Assert.Equal(memory, bus.ReadLong(0xC04000));
        Assert.Equal(0x7000u, cpu.State.A[7]);
        Assert.Equal(0x1008u, cpu.State.ProgramCounter);
    }

    public static IEnumerable<object[]> DisplacementNegations()
    {
        foreach (var model in new[] { M68kCpuModel.M68020, M68kCpuModel.M68EC020, M68kCpuModel.M68030 })
        foreach (var size in new[] { 1, 2, 4 })
        foreach (var displacement in new short[] { -4, 4 })
        foreach (var valueCase in new[] { 0, 1, 2, 3 })
            yield return new object[] { model, size, displacement, valueCase };
    }

    [Theory]
    [MemberData(nameof(DisplacementNegations))]
    public void NegateDisplacementHandlesZeroCarryAndMinimumSignedOverflow(M68kCpuModel model, int size, short displacement, int valueCase)
    {
        var mask = size == 1 ? 0xFFu : size == 2 ? 0xFFFFu : uint.MaxValue;
        var sign = (mask >> 1) + 1;
        var initial = valueCase == 0 ? 0u : valueCase == 1 ? 1u : valueCase == 2 ? mask : sign;
        var expected = valueCase == 0 ? 0u : valueCase == 1 ? mask : valueCase == 2 ? 1u : sign;
        var shift = 32 - size * 8;
        var guard = size == 1 ? 0xABCDEFu : size == 2 ? 0xCDEFu : 0u;
        var address = unchecked((uint)(0x4004 + displacement));
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, (ushort)(0x442C | ((size == 1 ? 0 : size == 2 ? 1 : 2) << 6)), unchecked((ushort)displacement));
        bus.WriteLong(address, (initial << shift) | guard);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[4] = 0x4004;
        cpu.State.StatusRegister = 0x271F;
        cpu.ExecuteInstruction();
        Assert.Equal((expected << shift) | guard, bus.ReadLong(address));
        Assert.Equal(0x4004u, cpu.State.A[4]);
        Assert.Equal(valueCase == 0 ? 0x2704 : valueCase == 1 ? 0x2719 : valueCase == 2 ? 0x2711 : 0x271B, cpu.State.StatusRegister);
        Assert.Equal(0x1004u, cpu.State.ProgramCounter);
    }

    [Theory]
    [MemberData(nameof(IndexedSubtracts))]
    public void CompareIndexedMemoryPreservesOperandsAndExtend(M68kCpuModel model, int size, bool addressIndex, int arithmeticCase)
    {
        var mask = size == 1 ? 0xFFu : size == 2 ? 0xFFFFu : uint.MaxValue;
        var initial = arithmeticCase == 0 ? 0u : arithmeticCase == 1 ? (mask >> 1) + 1 : 1u;
        var sourceAndGuard = size == 1 ? 0x01ABCDEFu : size == 2 ? 0x0001CDEFu : 1u;
        var destination = (0xABCDEF00u & ~mask) | initial;
        var address = addressIndex ? 0x3FF5u : 0x3FF9u;
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, (ushort)(0xB030 | ((size == 1 ? 0 : size == 2 ? 1 : 2) << 6)), addressIndex ? (ushort)0x9CFD : (ushort)0x12FD);
        bus.WriteLong(address, sourceAndGuard);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[0] = 0x4000;
        cpu.State.A[1] = 0xFFFFFFFE;
        cpu.State.D[1] = 0x1234FFFE;
        cpu.State.D[0] = destination;
        cpu.State.StatusRegister = 0x271F;
        cpu.ExecuteInstruction();
        Assert.Equal(destination, cpu.State.D[0]);
        Assert.Equal(sourceAndGuard, bus.ReadLong(address));
        Assert.Equal(0x4000u, cpu.State.A[0]);
        Assert.Equal(0xFFFFFFFEu, cpu.State.A[1]);
        Assert.Equal(0x1234FFFEu, cpu.State.D[1]);
        Assert.Equal(arithmeticCase == 0 ? 0x2719 : arithmeticCase == 1 ? 0x2712 : 0x2714, cpu.State.StatusRegister);
        Assert.Equal(0x1004u, cpu.State.ProgramCounter);
    }

    [Theory]
    [MemberData(nameof(IndexedSubtracts))]
    public void AndIndexedSourcePreservesUpperRegisterAndExtendFlag(M68kCpuModel model, int size, bool addressIndex, int resultCase)
    {
        var mask = size == 1 ? 0xFFu : size == 2 ? 0xFFFFu : uint.MaxValue;
        var source = resultCase == 0 ? 0u : resultCase == 1 ? (mask >> 1) + 2 : 0xFu;
        var initial = resultCase == 2 ? 0xA5u : mask;
        var expected = resultCase == 0 ? 0u : resultCase == 1 ? (mask >> 1) + 2 : 5u;
        var shift = 32 - size * 8;
        var guard = size == 1 ? 0xABCDEFu : size == 2 ? 0xCDEFu : 0u;
        var upper = 0xABCDEF00u & ~mask;
        var address = addressIndex ? 0x3FF5u : 0x3FF9u;
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, (ushort)(0xC030 | ((size == 1 ? 0 : size == 2 ? 1 : 2) << 6)), addressIndex ? (ushort)0x9CFD : (ushort)0x12FD);
        bus.WriteLong(address, (source << shift) | guard);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[0] = 0x4000;
        cpu.State.A[1] = 0xFFFFFFFE;
        cpu.State.D[1] = 0x1234FFFE;
        cpu.State.D[0] = upper | initial;
        cpu.State.StatusRegister = 0x271F;
        cpu.ExecuteInstruction();
        Assert.Equal(upper | expected, cpu.State.D[0]);
        Assert.Equal((source << shift) | guard, bus.ReadLong(address));
        Assert.Equal(0x4000u, cpu.State.A[0]);
        Assert.Equal(0xFFFFFFFEu, cpu.State.A[1]);
        Assert.Equal(0x1234FFFEu, cpu.State.D[1]);
        Assert.Equal(resultCase == 0 ? 0x2714 : resultCase == 1 ? 0x2718 : 0x2710, cpu.State.StatusRegister);
        Assert.Equal(0x1004u, cpu.State.ProgramCounter);
    }

    public static IEnumerable<object[]> PostincrementAddressSubtracts()
    {
        foreach (var model in new[] { M68kCpuModel.M68020, M68kCpuModel.M68EC020, M68kCpuModel.M68030 })
        foreach (var size in new[] { 2, 4 })
        foreach (var sourceRegister in new[] { 2, 7 })
        foreach (var alias in new[] { false, true })
        foreach (var valueCase in new[] { 0, 1, 2 })
            yield return new object[] { model, size, sourceRegister, alias, valueCase };
    }

    [Theory]
    [MemberData(nameof(PostincrementAddressSubtracts))]
    public void SubtractAddressPostincrementSignExtendsWordAndUsesUpdatedAliasedDestination(M68kCpuModel model, int size, int sourceRegister, bool alias, int valueCase)
    {
        var destinationRegister = alias ? sourceRegister : sourceRegister == 7 ? 2 : 7;
        var signedSource = valueCase == 0 ? 2 : valueCase == 1 ? -2 : size == 2 ? -32768 : 65538;
        var memory = size == 2 ? ((uint)(ushort)signedSource << 16) | 0xABCD : unchecked((uint)signedSource);
        var expected = unchecked((uint)((long)(alias ? 0x4000u + (uint)size : 0x10000u) - signedSource));
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, (ushort)((size == 2 ? 0x90D8 : 0x91D8) | (destinationRegister << 9) | sourceRegister));
        bus.WriteLong(0x4000, memory);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[sourceRegister] = 0x4000;
        if (!alias) cpu.State.A[destinationRegister] = 0x10000;
        cpu.State.StatusRegister = 0x271F;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, cpu.State.A[destinationRegister]);
        if (!alias) Assert.Equal(0x4000u + (uint)size, cpu.State.A[sourceRegister]);
        Assert.Equal(memory, bus.ReadLong(0x4000));
        Assert.Equal(0x271F, cpu.State.StatusRegister);
        Assert.Equal(0x1002u, cpu.State.ProgramCounter);
    }

    public static IEnumerable<object[]> IndexedSubtracts()
    {
        foreach (var model in new[] { M68kCpuModel.M68020, M68kCpuModel.M68EC020, M68kCpuModel.M68030 })
        foreach (var size in new[] { 1, 2, 4 })
        foreach (var addressIndex in new[] { false, true })
        foreach (var arithmeticCase in new[] { 0, 1, 2 })
            yield return new object[] { model, size, addressIndex, arithmeticCase };
    }

    [Theory]
    [MemberData(nameof(IndexedSubtracts))]
    public void SubtractIndexedMemoryUsesSignedScaledIndexAndPreservesUpperBits(M68kCpuModel model, int size, bool addressIndex, int arithmeticCase)
    {
        var mask = size == 1 ? 0xFFu : size == 2 ? 0xFFFFu : uint.MaxValue;
        var initial = arithmeticCase == 0 ? 0u : arithmeticCase == 1 ? (mask >> 1) + 1 : 1u;
        var expected = arithmeticCase == 0 ? mask : arithmeticCase == 1 ? mask >> 1 : 0u;
        var sourceAndGuard = size == 1 ? 0x01ABCDEFu : size == 2 ? 0x0001CDEFu : 1u;
        var upper = 0xABCDEF00u & ~mask;
        var address = addressIndex ? 0x3FF5u : 0x3FF9u;
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, (ushort)(0x9030 | ((size == 1 ? 0 : size == 2 ? 1 : 2) << 6)), addressIndex ? (ushort)0x9CFD : (ushort)0x12FD);
        bus.WriteLong(address, sourceAndGuard);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[0] = 0x4000;
        cpu.State.A[1] = 0xFFFFFFFE;
        cpu.State.D[1] = 0x1234FFFE;
        cpu.State.D[0] = upper | initial;
        cpu.State.StatusRegister = 0x271F;
        cpu.ExecuteInstruction();
        Assert.Equal(upper | expected, cpu.State.D[0]);
        Assert.Equal(sourceAndGuard, bus.ReadLong(address));
        Assert.Equal(0x4000u, cpu.State.A[0]);
        Assert.Equal(0xFFFFFFFEu, cpu.State.A[1]);
        Assert.Equal(0x1234FFFEu, cpu.State.D[1]);
        Assert.Equal(arithmeticCase == 0 ? 0x2719 : arithmeticCase == 1 ? 0x2702 : 0x2704, cpu.State.StatusRegister);
        Assert.Equal(0x1004u, cpu.State.ProgramCounter);
    }

    public static IEnumerable<object[]> DisplacementImmediateSubtracts()
    {
        foreach (var model in new[] { M68kCpuModel.M68020, M68kCpuModel.M68EC020, M68kCpuModel.M68030 })
        foreach (var displacement in new short[] { -4, 4 })
        foreach (var immediate in new[] { 1u, 0x10001u })
        foreach (var arithmeticCase in new[] { 0, 1, 2 })
            yield return new object[] { model, displacement, immediate, arithmeticCase };
    }

    [Theory]
    [MemberData(nameof(DisplacementImmediateSubtracts))]
    public void SubtractLongImmediateFetchesFullOperandBeforeSignedDisplacement(M68kCpuModel model, short displacement, uint immediate, int arithmeticCase)
    {
        var initial = arithmeticCase == 0 ? 0u : arithmeticCase == 1 ? 0x80000000u : immediate;
        var expected = arithmeticCase == 0 ? uint.MaxValue - immediate + 1 : arithmeticCase == 1 ? 0x80000000u - immediate : 0u;
        var address = unchecked((uint)(0x4004 + displacement));
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, 0x04AB, (ushort)(immediate >> 16), (ushort)immediate, unchecked((ushort)displacement));
        bus.WriteWord(address - 2, 0xAABB);
        bus.WriteLong(address, initial);
        bus.WriteWord(address + 4, 0xCCDD);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[3] = 0x4004;
        cpu.State.StatusRegister = 0x271F;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadLong(address));
        Assert.Equal(0xAABB, bus.ReadWord(address - 2));
        Assert.Equal(0xCCDD, bus.ReadWord(address + 4));
        Assert.Equal(0x4004u, cpu.State.A[3]);
        Assert.Equal(arithmeticCase == 0 ? 0x2719 : arithmeticCase == 1 ? 0x2702 : 0x2704, cpu.State.StatusRegister);
        Assert.Equal(0x1008u, cpu.State.ProgramCounter);
    }

    public static IEnumerable<object[]> DisplacementPostincrementMoves()
        => PredecrementDestinationMoves().Where(data => (int)data[1] != 4);

    [Theory]
    [MemberData(nameof(DisplacementPostincrementMoves))]
    public void MoveDisplacementSourcePrecedesAliasedPostincrement(M68kCpuModel model, int size, int destinationRegister, bool alias, bool zero)
    {
        var bus = new Copper68kTestBus();
        var sourceRegister = alias ? destinationRegister : 0;
        bus.WriteWords(0x1000, (ushort)((size == 1 ? 0x10E8 : 0x30E8) | (destinationRegister << 9) | sourceRegister), 0xFFFC);
        var destination = alias ? 0x4008u : 0x5000u;
        bus.WriteLong(0x4004, zero ? 0u : 0x80010203u);
        bus.WriteLong(0x4008, 0xAABBCCDD);
        bus.WriteLong(0x5000, 0x99AABBCC);
        var expected = Enumerable.Range(0, 8).Select(i => 0x4004u + (uint)i)
            .Concat(Enumerable.Range(0, 4).Select(i => 0x5000u + (uint)i))
            .ToDictionary(address => address, address => (byte)(bus.ReadWord(address) >> 8));
        for (var i = 0; i < size; i++) expected[destination + (uint)i] = zero ? (byte)0 : new byte[] { 0x80, 1 }[i];
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[sourceRegister] = 0x4008;
        if (!alias) cpu.State.A[destinationRegister] = destination;
        cpu.State.StatusRegister = 0x2713;
        cpu.ExecuteInstruction();
        if (!alias) Assert.Equal(0x4008u, cpu.State.A[sourceRegister]);
        Assert.Equal(destination + (uint)(size == 1 && destinationRegister == 7 ? 2 : size), cpu.State.A[destinationRegister]);
        Assert.Equal(zero ? 0x2714 : 0x2718, cpu.State.StatusRegister);
        Assert.Equal(0x1004u, cpu.State.ProgramCounter);
        foreach (var pair in expected) Assert.Equal(pair.Value, (byte)(bus.ReadWord(pair.Key) >> 8));
    }

    [Theory]
    [MemberData(nameof(PostIncrementAdds))]
    public void AndImmediatePostincrementFetchesCorrectWidthAndPreservesExtend(M68kCpuModel model, int size, int addressRegister, int resultCase)
    {
        var mask = size == 1 ? 0xFFu : size == 2 ? 0xFFFFu : uint.MaxValue;
        var immediate = resultCase == 0 ? 0u : resultCase == 1 ? (mask >> 1) + 2 : 0xFu;
        var initial = resultCase == 1 ? mask : 0xA5u;
        var expected = resultCase == 0 ? 0u : resultCase == 1 ? (mask >> 1) + 2 : 5u;
        var shift = 32 - size * 8;
        var guard = size == 1 ? 0x00ABCDEFu : size == 2 ? 0x0000CDEFu : 0u;
        var opcode = (ushort)(0x0218 | ((size == 1 ? 0 : size == 2 ? 1 : 2) << 6) | addressRegister);
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, size == 4
            ? new ushort[] { opcode, (ushort)(immediate >> 16), (ushort)immediate }
            : new ushort[] { opcode, size == 1 ? (ushort)(0xAB00u | immediate) : (ushort)immediate });
        bus.WriteLong(0x4000, (initial << shift) | guard);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[addressRegister] = 0x4000;
        cpu.State.StatusRegister = 0x271F;
        cpu.ExecuteInstruction();
        Assert.Equal((expected << shift) | guard, bus.ReadLong(0x4000));
        Assert.Equal(0x4000u + (uint)(size == 1 && addressRegister == 7 ? 2 : size), cpu.State.A[addressRegister]);
        Assert.Equal(resultCase == 0 ? 0x2714 : resultCase == 1 ? 0x2718 : 0x2710, cpu.State.StatusRegister);
        Assert.Equal(size == 4 ? 0x1006u : 0x1004u, cpu.State.ProgramCounter);
    }

    public static IEnumerable<object[]> PostincrementTests()
    {
        foreach (var model in new[] { M68kCpuModel.M68020, M68kCpuModel.M68EC020, M68kCpuModel.M68030 })
        foreach (var size in new[] { 2, 4 })
        foreach (var register in new[] { 5, 7 })
        foreach (var valueCase in new[] { 0, 1, 2 })
            yield return new object[] { model, size, register, valueCase };
    }

    [Theory]
    [MemberData(nameof(PostincrementTests))]
    public void TestPostincrementReadsOnlyOperandWidthAndAdvancesOnce(M68kCpuModel model, int size, int register, int valueCase)
    {
        var value = valueCase == 0 ? 0u : valueCase == 1 ? 7u : size == 2 ? 0x8001u : 0x80000001u;
        var memory = size == 2 ? (value << 16) | 0xFFFF : value;
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, (ushort)((size == 2 ? 0x4A58 : 0x4A98) | register));
        bus.WriteLong(0x4000, memory);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[register] = 0x4000;
        cpu.State.StatusRegister = 0x271F;
        cpu.ExecuteInstruction();
        Assert.Equal(valueCase == 0 ? 0x2714 : valueCase == 1 ? 0x2710 : 0x2718, cpu.State.StatusRegister);
        Assert.Equal(memory, bus.ReadLong(0x4000));
        Assert.Equal(0x4000u + (uint)size, cpu.State.A[register]);
        Assert.Equal(0x1002u, cpu.State.ProgramCounter);
    }

    public static IEnumerable<object[]> IndexedAdds()
    {
        foreach (var model in new[] { M68kCpuModel.M68020, M68kCpuModel.M68EC020, M68kCpuModel.M68030 })
        foreach (var size in new[] { 1, 2 })
        foreach (var addressIndex in new[] { false, true })
        foreach (var arithmeticCase in new[] { 0, 1, 2 })
            yield return new object[] { model, size, addressIndex, arithmeticCase };
    }

    [Theory]
    [MemberData(nameof(IndexedAdds))]
    public void AddIndexedByteOrWordPreservesUpperRegisterAndUsesSignedScaledIndex(M68kCpuModel model, int size, bool addressIndex, int arithmeticCase)
    {
        var mask = size == 1 ? 0xFFu : 0xFFFFu;
        var source = arithmeticCase == 0 ? mask : arithmeticCase == 1 ? mask >> 1 : 5u;
        var expected = arithmeticCase == 0 ? 0u : arithmeticCase == 1 ? (mask >> 1) + 1 : 6u;
        var shift = size == 1 ? 24 : 16;
        var guard = size == 1 ? 0xABCDEFu : 0xCDEFu;
        var upper = 0xABCDEF00u & ~mask;
        var address = addressIndex ? 0x3FF5u : 0x3FF9u;
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, size == 1 ? (ushort)0xD030 : (ushort)0xD070, addressIndex ? (ushort)0x9CFD : (ushort)0x12FD);
        bus.WriteLong(address, (source << shift) | guard);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[0] = 0x4000;
        cpu.State.A[1] = 0xFFFFFFFE;
        cpu.State.D[1] = 0x1234FFFE;
        cpu.State.D[0] = upper | 1u;
        cpu.State.StatusRegister = 0x271F;
        cpu.ExecuteInstruction();
        Assert.Equal(upper | expected, cpu.State.D[0]);
        Assert.Equal((source << shift) | guard, bus.ReadLong(address));
        Assert.Equal(0x4000u, cpu.State.A[0]);
        Assert.Equal(0xFFFFFFFEu, cpu.State.A[1]);
        Assert.Equal(0x1234FFFEu, cpu.State.D[1]);
        Assert.Equal(arithmeticCase == 0 ? 0x2715 : arithmeticCase == 1 ? 0x270A : 0x2700, cpu.State.StatusRegister);
        Assert.Equal(0x1004u, cpu.State.ProgramCounter);
    }

    public static IEnumerable<object[]> ArithmeticByteShifts()
    {
        var cases = new (byte value, int count, byte result, ushort flags)[]
        {
            (0x81, 1, 0xC0, 0x19), (0x80, 1, 0xC0, 0x08),
            (0x01, 1, 0x00, 0x15), (0x7E, 1, 0x3F, 0x00),
            (0x80, 8, 0xFF, 0x19), (0x7F, 8, 0x00, 0x04),
            (0xFF, 7, 0xFF, 0x19), (0x00, 8, 0x00, 0x04)
        };
        foreach (var model in new[] { M68kCpuModel.M68020, M68kCpuModel.M68EC020, M68kCpuModel.M68030 })
        foreach (var c in cases)
            yield return new object[] { model, c.value, c.count, c.result, c.flags };
    }

    [Theory]
    [MemberData(nameof(ArithmeticByteShifts))]
    public void ArithmeticByteShiftPreservesSignAndUpperBitsAndReportsLastBit(M68kCpuModel model, byte value, int count, byte result, ushort flags)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, (ushort)(0xE000 | ((count & 7) << 9)));
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.D[0] = 0xAABBCC00u | value;
        cpu.State.StatusRegister = 0x271F;
        cpu.ExecuteInstruction();
        Assert.Equal(0xAABBCC00u | result, cpu.State.D[0]);
        Assert.Equal(0x2700 | flags, cpu.State.StatusRegister);
        Assert.Equal(0x1002u, cpu.State.ProgramCounter);
    }

    public static IEnumerable<object[]> IndirectSubtracts()
    {
        foreach (var model in new[] { M68kCpuModel.M68020, M68kCpuModel.M68EC020, M68kCpuModel.M68030 })
        foreach (var size in new[] { 1, 2, 4 })
        foreach (var arithmeticCase in new[] { 0, 1, 2 })
            yield return new object[] { model, size, arithmeticCase };
    }

    [Theory]
    [MemberData(nameof(IndirectSubtracts))]
    public void SubtractFromIndirectTruncatesRegisterOperandAndPreservesAddress(M68kCpuModel model, int size, int arithmeticCase)
    {
        var mask = size == 1 ? 0xFFu : size == 2 ? 0xFFFFu : uint.MaxValue;
        var initial = arithmeticCase == 0 ? 0u : arithmeticCase == 1 ? (mask >> 1) + 1 : 1u;
        var expected = arithmeticCase == 0 ? mask : arithmeticCase == 1 ? mask >> 1 : 0u;
        var shift = 32 - size * 8;
        var guard = size == 1 ? 0x00ABCDEFu : size == 2 ? 0x0000CDEFu : 0u;
        var source = ~mask | 1u;
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, (ushort)(0x9110 | ((size == 1 ? 0 : size == 2 ? 1 : 2) << 6)));
        bus.WriteLong(0x4001, (initial << shift) | guard);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[0] = 0x4001;
        cpu.State.D[0] = source;
        cpu.State.StatusRegister = 0x271F;
        cpu.ExecuteInstruction();
        Assert.Equal((expected << shift) | guard, bus.ReadLong(0x4001));
        Assert.Equal(source, cpu.State.D[0]);
        Assert.Equal(0x4001u, cpu.State.A[0]);
        Assert.Equal(arithmeticCase == 0 ? 0x2719 : arithmeticCase == 1 ? 0x2702 : 0x2704, cpu.State.StatusRegister);
        Assert.Equal(0x1002u, cpu.State.ProgramCounter);
    }

    public static IEnumerable<object[]> AddressQuickSubtracts()
    {
        foreach (var model in new[] { M68kCpuModel.M68020, M68kCpuModel.M68EC020, M68kCpuModel.M68030 })
        foreach (var register in new[] { 0, 7 })
        foreach (var immediate in new[] { 1, 8 })
        foreach (var initial in new[] { 0x10000u, 0u, 0x80000000u })
            yield return new object[] { model, register, immediate, initial };
    }

    [Theory]
    [MemberData(nameof(AddressQuickSubtracts))]
    public void SubtractQuickWordFromAddressUsesAllThirtyTwoBitsAndPreservesCcr(M68kCpuModel model, int register, int immediate, uint initial)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, (ushort)(0x5148 | ((immediate & 7) << 9) | register));
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[register] = initial;
        cpu.State.StatusRegister = 0x271F;
        cpu.ExecuteInstruction();
        Assert.Equal(unchecked(initial - (uint)immediate), cpu.State.A[register]);
        Assert.Equal(0x271F, cpu.State.StatusRegister);
        Assert.Equal(0x1002u, cpu.State.ProgramCounter);
    }

    public static IEnumerable<object[]> PostIncrementByteMoves()
        => PostIncrementMoves().Where(data => (int)data[1] == 1)
            .Select(data => new object[] { data[0], data[2], data[3], data[4] });

    [Theory]
    [MemberData(nameof(PostIncrementByteMoves))]
    public void MoveBytePostincrementResolvesAliasedDisplacementAfterIncrement(M68kCpuModel model, int sourceRegister, bool alias, bool zero)
    {
        var bus = new Copper68kTestBus();
        var destinationRegister = alias ? sourceRegister : 0;
        bus.WriteWords(0x1000, (ushort)(0x1158 | (destinationRegister << 9) | sourceRegister), 0xFFFC);
        var increment = sourceRegister == 7 ? 2u : 1u;
        var destination = alias ? 0x4000u + increment : 0x5000u;
        bus.WriteLong(0x4000, 0xAABBCCDD);
        bus.WriteLong(0x4004, zero ? 0x00112233u : 0x80112233u);
        bus.WriteLong(0x5000, 0x99AABBCC);
        var expected = Enumerable.Range(0, 8).Select(i => 0x4000u + (uint)i)
            .Concat(Enumerable.Range(0, 4).Select(i => 0x5000u + (uint)i))
            .ToDictionary(address => address, address => (byte)(bus.ReadWord(address) >> 8));
        expected[destination] = zero ? (byte)0 : (byte)0x80;
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[sourceRegister] = 0x4004;
        if (!alias) cpu.State.A[destinationRegister] = 0x5004;
        cpu.State.StatusRegister = 0x2713;
        cpu.ExecuteInstruction();
        Assert.Equal(0x4004u + increment, cpu.State.A[sourceRegister]);
        if (!alias) Assert.Equal(0x5004u, cpu.State.A[destinationRegister]);
        Assert.Equal(zero ? 0x2714 : 0x2718, cpu.State.StatusRegister);
        Assert.Equal(0x1004u, cpu.State.ProgramCounter);
        foreach (var pair in expected) Assert.Equal(pair.Value, (byte)(bus.ReadWord(pair.Key) >> 8));
    }

    [Theory]
    [MemberData(nameof(PostIncrementMoves))]
    public void MovePredecrementResolvesSignedDisplacementAfterAliasedUpdate(M68kCpuModel model, int size, int sourceRegister, bool alias, bool zero)
    {
        var bus = new Copper68kTestBus();
        var destinationRegister = alias ? sourceRegister : 0;
        var prefix = size == 1 ? 0x1000 : size == 2 ? 0x3000 : 0x2000;
        bus.WriteWords(0x1000, (ushort)(prefix | (destinationRegister << 9) | 0x160 | sourceRegister), 0xFFFC);
        var decrement = size == 1 && sourceRegister == 7 ? 2u : (uint)size;
        var source = 0x4008u - decrement;
        var destination = alias ? source - 4 : 0x5000u;
        bus.WriteLong(0x4000, 0xAABBCCDD);
        bus.WriteLong(0x4004, 0x55667788);
        bus.WriteLong(0x4008, 0x99AABBCC);
        bus.WriteLong(source, zero ? 0u : 0x80010203u);
        bus.WriteLong(0x5000, 0xAABBCCDD);
        bus.WriteLong(0x5004, 0xDDEEFF11);
        var expected = Enumerable.Range(0, 12).Select(i => 0x4000u + (uint)i)
            .Concat(Enumerable.Range(0, 8).Select(i => 0x5000u + (uint)i))
            .ToDictionary(address => address, address => (byte)(bus.ReadWord(address) >> 8));
        for (var i = 0; i < size; i++) expected[destination + (uint)i] = zero ? (byte)0 : new byte[] { 0x80, 1, 2, 3 }[i];
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[sourceRegister] = 0x4008;
        if (!alias) cpu.State.A[destinationRegister] = 0x5004;
        cpu.State.StatusRegister = 0x2713;
        cpu.ExecuteInstruction();
        Assert.Equal(source, cpu.State.A[sourceRegister]);
        if (!alias) Assert.Equal(0x5004u, cpu.State.A[destinationRegister]);
        Assert.Equal(zero ? 0x2714 : 0x2718, cpu.State.StatusRegister);
        Assert.Equal(0x1004u, cpu.State.ProgramCounter);
        foreach (var pair in expected) Assert.Equal(pair.Value, (byte)(bus.ReadWord(pair.Key) >> 8));
    }

    [Theory]
    [MemberData(nameof(PostIncrementMoves))]
    public void MovePredecrementThenPostincrementAppliesAliasedSideEffectsInOrder(M68kCpuModel model, int size, int sourceRegister, bool alias, bool zero)
    {
        var bus = new Copper68kTestBus();
        var destinationRegister = alias ? sourceRegister : sourceRegister == 7 ? 2 : 7;
        var prefix = size == 1 ? 0x1000 : size == 2 ? 0x3000 : 0x2000;
        bus.WriteWords(0x1000, (ushort)(prefix | (destinationRegister << 9) | 0xE0 | sourceRegister));
        var sourceIncrement = size == 1 && sourceRegister == 7 ? 2u : (uint)size;
        var destinationIncrement = size == 1 && destinationRegister == 7 ? 2u : (uint)size;
        var source = 0x4004u - sourceIncrement;
        var destination = alias ? source : 0x5000u;
        bus.WriteLong(0x4000, 0xAABBCCDD);
        bus.WriteLong(0x4004, 0x55667788);
        bus.WriteLong(source, zero ? 0u : 0x80010203u);
        bus.WriteLong(0x5000, 0x99AABBCC);
        bus.WriteLong(0x5004, 0xDDEEFF11);
        var expected = Enumerable.Range(0, 8).Select(i => 0x4000u + (uint)i)
            .Concat(Enumerable.Range(0, 8).Select(i => 0x5000u + (uint)i))
            .ToDictionary(address => address, address => (byte)(bus.ReadWord(address) >> 8));
        for (var i = 0; i < size; i++) expected[destination + (uint)i] = zero ? (byte)0 : new byte[] { 0x80, 1, 2, 3 }[i];
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[sourceRegister] = 0x4004;
        if (!alias) cpu.State.A[destinationRegister] = 0x5000;
        cpu.State.StatusRegister = 0x2713;
        cpu.ExecuteInstruction();
        Assert.Equal(alias ? 0x4004u : source, cpu.State.A[sourceRegister]);
        Assert.Equal(destination + destinationIncrement, cpu.State.A[destinationRegister]);
        Assert.Equal(zero ? 0x2714 : 0x2718, cpu.State.StatusRegister);
        Assert.Equal(0x1002u, cpu.State.ProgramCounter);
        foreach (var pair in expected) Assert.Equal(pair.Value, (byte)(bus.ReadWord(pair.Key) >> 8));
    }

    public static IEnumerable<object[]> IndirectWordTests()
    {
        foreach (var model in new[] { M68kCpuModel.M68020, M68kCpuModel.M68EC020, M68kCpuModel.M68030 })
        foreach (var value in new ushort[] { 0, 7, 0x8001 })
            yield return new object[] { model, value };
    }

    [Theory]
    [MemberData(nameof(IndirectWordTests))]
    public void TestWordIndirectChangesOnlyNzvcFlags(M68kCpuModel model, ushort value)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, 0x4A51);
        var memory = 0xAA0000BBu | ((uint)value << 8);
        bus.WriteLong(0x4000, memory);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[1] = 0x4001;
        cpu.State.StatusRegister = 0x271F;
        cpu.ExecuteInstruction();
        Assert.Equal(value == 0 ? 0x2714 : value == 7 ? 0x2710 : 0x2718, cpu.State.StatusRegister);
        Assert.Equal(memory, bus.ReadLong(0x4000));
        Assert.Equal(0x4001u, cpu.State.A[1]);
        Assert.Equal(0x1002u, cpu.State.ProgramCounter);
    }

    [Theory]
    [MemberData(nameof(PostIncrementAdds))]
    public void SubtractPostIncrementSourcePreservesUpperRegisterAndMemory(M68kCpuModel model, int size, int addressRegister, int arithmeticCase)
    {
        var mask = size == 1 ? 0xFFu : size == 2 ? 0xFFFFu : uint.MaxValue;
        var sign = (mask >> 1) + 1;
        var initial = arithmeticCase == 0 ? 0u : arithmeticCase == 1 ? sign : 1u;
        var expected = arithmeticCase == 0 ? mask : arithmeticCase == 1 ? mask >> 1 : 0u;
        var upper = 0xAABBCCDDu & ~mask;
        var sourceAndGuard = size == 1 ? 0x01ABCDEFu : size == 2 ? 0x0001CDEFu : 1u;
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, (ushort)(0x9618 | ((size == 1 ? 0 : size == 2 ? 1 : 2) << 6) | addressRegister));
        bus.WriteLong(0x4000, sourceAndGuard);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[addressRegister] = 0x4000;
        cpu.State.D[3] = upper | initial;
        cpu.State.StatusRegister = 0x271F;
        cpu.ExecuteInstruction();
        Assert.Equal(upper | expected, cpu.State.D[3]);
        Assert.Equal(sourceAndGuard, bus.ReadLong(0x4000));
        Assert.Equal(0x4000u + (uint)(size == 1 && addressRegister == 7 ? 2 : size), cpu.State.A[addressRegister]);
        Assert.Equal(arithmeticCase == 0 ? 0x2719 : arithmeticCase == 1 ? 0x2702 : 0x2704, cpu.State.StatusRegister);
        Assert.Equal(0x1002u, cpu.State.ProgramCounter);
    }

    [Theory]
    [MemberData(nameof(MemorySourceByteAdds))]
    public void AddByteFromMemoryPreservesRegisterUpperBits(M68kCpuModel model, short displacement, int arithmeticCase)
    {
        var bus = new Copper68kTestBus();
        if (displacement == 0) bus.WriteWords(0x1000, 0xD410);
        else bus.WriteWords(0x1000, 0xD428, unchecked((ushort)displacement));
        var address = unchecked((uint)(0x4000 + displacement));
        var source = arithmeticCase == 0 ? 0xFFu : arithmeticCase == 1 ? 0x7Fu : 5u;
        var expected = arithmeticCase == 0 ? 0u : arithmeticCase == 1 ? 0x80u : 6u;
        bus.WriteLong(address - 1, 0xAA00CCDD | (source << 16));
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[0] = 0x4000;
        cpu.State.D[2] = 0xABCDEF01;
        cpu.State.StatusRegister = 0x271F;
        cpu.ExecuteInstruction();
        Assert.Equal(0xAA00CCDD | (source << 16), bus.ReadLong(address - 1));
        Assert.Equal(0x4000u, cpu.State.A[0]);
        Assert.Equal(0xABCDEF00u | expected, cpu.State.D[2]);
        Assert.Equal(arithmeticCase == 0 ? 0x2715 : arithmeticCase == 1 ? 0x270A : 0x2700, cpu.State.StatusRegister);
        Assert.Equal(displacement == 0 ? 0x1002u : 0x1004u, cpu.State.ProgramCounter);
    }

    public static IEnumerable<object[]> MemorySourceByteAdds()
    {
        foreach (var data in DisplacementByteAdds()) yield return data;
        foreach (var model in new[] { M68kCpuModel.M68020, M68kCpuModel.M68EC020, M68kCpuModel.M68030 })
        foreach (var arithmeticCase in new[] { 0, 1, 2 })
            yield return new object[] { model, (short)0, arithmeticCase };
    }

    public static IEnumerable<object[]> IndexedSourceMoves()
    {
        foreach (var model in new[] { M68kCpuModel.M68020, M68kCpuModel.M68EC020, M68kCpuModel.M68030 })
        foreach (var size in new[] { 1, 2, 4 })
        foreach (var addressIndex in new[] { false, true })
        foreach (var zero in new[] { false, true })
            yield return new object[] { model, size, addressIndex, zero };
    }

    [Theory]
    [MemberData(nameof(IndexedSourceMoves))]
    public void MoveIndexedSourceSignExtendsWordOrLongIndexAndPreservesDestinationGuards(M68kCpuModel model, int size, bool addressIndex, bool zero)
    {
        var bus = new Copper68kTestBus();
        var prefix = size == 1 ? 0x1000 : size == 2 ? 0x3000 : 0x2000;
        bus.WriteWords(0x1000, (ushort)(prefix | 0x4B0), addressIndex ? (ushort)0x9C06 : (ushort)0x12FC);
        var source = addressIndex ? 0x3FFAu : 0x3FF8u;
        bus.WriteLong(source, zero ? 0u : 0x80010203u);
        bus.WriteLong(0x5000, 0xAABBCCDD);
        bus.WriteLong(0x5004, 0xEEFF6677);
        var expected = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x66, 0x77 };
        for (var i = 0; i < size; i++) expected[i + 1] = zero ? (byte)0 : new byte[] { 0x80, 1, 2, 3 }[i];
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[0] = 0x4000;
        cpu.State.A[1] = 0xFFFFFFFD;
        cpu.State.A[2] = 0x5001;
        cpu.State.D[1] = 0x1234FFFE;
        cpu.State.StatusRegister = 0x2713;
        cpu.ExecuteInstruction();
        for (var i = 0; i < expected.Length; i++) Assert.Equal(expected[i], (byte)(bus.ReadWord(0x5000u + (uint)i) >> 8));
        Assert.Equal(0x4000u, cpu.State.A[0]);
        Assert.Equal(0xFFFFFFFDu, cpu.State.A[1]);
        Assert.Equal(0x5001u, cpu.State.A[2]);
        Assert.Equal(0x1234FFFEu, cpu.State.D[1]);
        Assert.Equal(zero ? 0x2714 : 0x2718, cpu.State.StatusRegister);
        Assert.Equal(0x1004u, cpu.State.ProgramCounter);
    }

    public static IEnumerable<object[]> IndexedBitOperations()
    {
        foreach (var model in new[] { M68kCpuModel.M68020, M68kCpuModel.M68EC020, M68kCpuModel.M68030 })
        foreach (var operation in new[] { 0, 1, 2, 3 })
        foreach (var bit in new[] { 8, 31 })
        foreach (var set in new[] { false, true })
            yield return new object[] { model, operation, bit, set };
    }

    [Theory]
    [MemberData(nameof(IndexedBitOperations))]
    public void IndexedBitOperationUsesOriginalModuloEightBitAndPreservesOtherFlags(M68kCpuModel model, int operation, int bit, bool set)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, (ushort)(0x0B30 | (operation << 6)), 0x1406); // 6(A0,D1.W*4)
        var original = set ? 0xA5u : 0x5Au;
        var changed = bit == 8 ? (set ? 0xA4u : 0x5Bu) : (set ? 0x25u : 0xDAu);
        var expected = operation == 1 || operation == 2 && set || operation == 3 && !set ? changed : original;
        bus.WriteLong(0x3FFD, 0xCC00DDEE | (original << 16));
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[0] = 0x4000;
        cpu.State.D[1] = 0x1234FFFE;
        cpu.State.D[5] = (uint)bit;
        cpu.State.StatusRegister = 0x271F;
        cpu.ExecuteInstruction();
        Assert.Equal(0xCC00DDEE | (expected << 16), bus.ReadLong(0x3FFD));
        Assert.Equal(set ? 0x271B : 0x271F, cpu.State.StatusRegister);
        Assert.Equal(0x4000u, cpu.State.A[0]);
        Assert.Equal(0x1234FFFEu, cpu.State.D[1]);
        Assert.Equal((uint)bit, cpu.State.D[5]);
        Assert.Equal(0x1004u, cpu.State.ProgramCounter);
    }

    public static IEnumerable<object[]> PostincrementLogicalOperations()
    {
        foreach (var and in new[] { false, true })
        foreach (var model in new[] { M68kCpuModel.M68020, M68kCpuModel.M68EC020, M68kCpuModel.M68030 })
        foreach (var size in new[] { 1, 2, 4 })
        foreach (var addressRegister in new[] { 4, 7 })
        foreach (var resultCase in new[] { 0, 1, 2 })
            yield return new object[] { model, size, addressRegister, resultCase, and };
    }

    [Theory]
    [MemberData(nameof(PostincrementLogicalOperations))]
    public void LogicalPostincrementPreservesExtendAndUpdatesOnlyOperandWidth(M68kCpuModel model, int size, int addressRegister, int resultCase, bool and)
    {
        var mask = size == 1 ? 0xFFu : size == 2 ? 0xFFFFu : uint.MaxValue;
        var initial = resultCase == 0 ? 0u : resultCase == 1 ? (mask >> 1) + 1 : 0x40u;
        var source = (~mask) | (resultCase == 0 ? 0u : 1u);
        var expected = resultCase == 0 ? 0u : resultCase == 1 ? (mask >> 1) + 2 : 0x41u;
        if (and)
        {
            initial = resultCase == 0 ? mask : resultCase == 1 ? (mask >> 1) + 3 : 0x43u;
            source = (~mask) | (resultCase == 0 ? 0u : resultCase == 1 ? (mask >> 1) + 2 : 0x11u);
            expected = resultCase == 0 ? 0u : resultCase == 1 ? (mask >> 1) + 1 : 1u;
        }
        var shift = 32 - size * 8;
        var guard = size == 1 ? 0x00ABCDEFu : size == 2 ? 0x0000CDEFu : 0u;
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, (ushort)((and ? 0xC318 : 0x8318) | ((size == 1 ? 0 : size == 2 ? 1 : 2) << 6) | addressRegister));
        bus.WriteLong(0x4000, (initial << shift) | guard);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[addressRegister] = 0x4000;
        cpu.State.D[1] = source;
        cpu.State.StatusRegister = 0x271F;
        cpu.ExecuteInstruction();
        Assert.Equal((expected << shift) | guard, bus.ReadLong(0x4000));
        Assert.Equal(0x4000u + (uint)(size == 1 && addressRegister == 7 ? 2 : size), cpu.State.A[addressRegister]);
        Assert.Equal(source, cpu.State.D[1]);
        Assert.Equal(resultCase == 0 ? 0x2714 : resultCase == 1 ? 0x2718 : 0x2710, cpu.State.StatusRegister);
        Assert.Equal(0x1002u, cpu.State.ProgramCounter);
    }

    public static IEnumerable<object[]> PredecrementDestinationMoves()
    {
        foreach (var model in new[] { M68kCpuModel.M68020, M68kCpuModel.M68EC020, M68kCpuModel.M68030 })
        foreach (var size in new[] { 1, 2, 4 })
        foreach (var destinationRegister in new[] { 1, 7 })
        foreach (var alias in new[] { false, true })
        foreach (var zero in new[] { false, true })
            yield return new object[] { model, size, destinationRegister, alias, zero };
    }

    [Theory]
    [MemberData(nameof(PredecrementDestinationMoves))]
    public void MoveReadsAliasedSourceBeforePredecrementDestination(M68kCpuModel model, int size, int destinationRegister, bool alias, bool zero)
    {
        var bus = new Copper68kTestBus();
        var sourceRegister = alias ? destinationRegister : 0;
        var prefix = size == 1 ? 0x1000 : size == 2 ? 0x3000 : 0x2000;
        bus.WriteWords(0x1000, (ushort)(prefix | (destinationRegister << 9) | 0x110 | sourceRegister));
        bus.WriteLong(0x4000, 0xAABBCCDD);
        bus.WriteLong(0x4004, zero ? 0u : 0x80010203u);
        bus.WriteLong(0x5000, zero ? 0u : 0x80010203u);
        var decrement = size == 1 && destinationRegister == 7 ? 2u : (uint)size;
        var destination = 0x4004u - decrement;
        var expected = Enumerable.Range(0, 8).ToDictionary(i => 0x4000u + (uint)i, i => (byte)(bus.ReadWord(0x4000u + (uint)i) >> 8));
        for (var i = 0; i < size; i++) expected[destination + (uint)i] = zero ? (byte)0 : new byte[] { 0x80, 1, 2, 3 }[i];
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[destinationRegister] = 0x4004;
        if (!alias) cpu.State.A[sourceRegister] = 0x5000;
        cpu.State.StatusRegister = 0x2713;
        cpu.ExecuteInstruction();
        Assert.Equal(destination, cpu.State.A[destinationRegister]);
        if (!alias) Assert.Equal(0x5000u, cpu.State.A[sourceRegister]);
        Assert.Equal(zero ? 0x2714 : 0x2718, cpu.State.StatusRegister);
        Assert.Equal(0x1002u, cpu.State.ProgramCounter);
        foreach (var pair in expected) Assert.Equal(pair.Value, (byte)(bus.ReadWord(pair.Key) >> 8));
        Assert.Equal(zero ? 0u : 0x80010203u, bus.ReadLong(0x5000));
    }

    public static IEnumerable<object[]> DisplacementByteAdds()
    {
        foreach (var model in new[] { M68kCpuModel.M68020, M68kCpuModel.M68EC020, M68kCpuModel.M68030 })
        foreach (var displacement in new short[] { -3, 3 })
        foreach (var arithmeticCase in new[] { 0, 1, 2 })
            yield return new object[] { model, displacement, arithmeticCase };
    }

    [Theory]
    [MemberData(nameof(DisplacementByteAdds))]
    public void AddByteDisplacementUsesSignedAddressAndOnlyLowSourceByte(M68kCpuModel model, short displacement, int arithmeticCase)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, 0xD728, unchecked((ushort)displacement)); // ADD.B D3,d16(A0)
        var address = unchecked((uint)(0x4000 + displacement));
        var initial = arithmeticCase == 0 ? 0xFFu : arithmeticCase == 1 ? 0x7Fu : 5u;
        var expected = arithmeticCase == 0 ? 0u : arithmeticCase == 1 ? 0x80u : 6u;
        bus.WriteLong(address - 1, 0xAA00CCDD | (initial << 16));
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[0] = 0x4000;
        cpu.State.D[3] = 0xABCDEF01;
        cpu.State.StatusRegister = 0x271F;
        cpu.ExecuteInstruction();
        Assert.Equal(0xAA00CCDD | (expected << 16), bus.ReadLong(address - 1));
        Assert.Equal(0x4000u, cpu.State.A[0]);
        Assert.Equal(0xABCDEF01u, cpu.State.D[3]);
        Assert.Equal(arithmeticCase == 0 ? 0x2715 : arithmeticCase == 1 ? 0x270A : 0x2700, cpu.State.StatusRegister);
        Assert.Equal(0x1004u, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(M68kCpuModel.M68020, 0, 0xAABBCC00u, 0x4003u)]
    [InlineData(M68kCpuModel.M68EC020, 0, 0xAABBCC00u, 0x4003u)]
    [InlineData(M68kCpuModel.M68030, 0, 0xAABBCC00u, 0x4003u)]
    [InlineData(M68kCpuModel.M68020, 7, 0xAABB00DDu, 0x4002u)]
    [InlineData(M68kCpuModel.M68EC020, 7, 0xAABB00DDu, 0x4002u)]
    [InlineData(M68kCpuModel.M68030, 7, 0xAABB00DDu, 0x4002u)]
    public void ClearBytePredecrementKeepsStackAlignedAndSurroundingBytes(M68kCpuModel model, int register, uint expected, uint address)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, (ushort)(0x4220 | register));
        bus.WriteLong(0x4000, 0xAABBCCDD);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x4004);
        cpu.State.A[register] = 0x4004;
        cpu.State.StatusRegister = 0x271B;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadLong(0x4000));
        Assert.Equal(address, cpu.State.A[register]);
        Assert.Equal(0x2714, cpu.State.StatusRegister);
    }

    public static IEnumerable<object[]> ExtendedSourceMoves()
    {
        foreach (var model in new[] { M68kCpuModel.M68020, M68kCpuModel.M68EC020, M68kCpuModel.M68030 })
        foreach (var sourceForm in new[] { 0, 1, 2, 3, 4 })
        foreach (var zero in new[] { false, true })
            yield return new object[] { model, sourceForm, zero };
    }

    [Theory]
    [MemberData(nameof(ExtendedSourceMoves))]
    public void MemorySourceMoveMustNotBeDecodedAsImmediate(M68kCpuModel model, int sourceForm, bool zero)
    {
        var bus = new ZeroWaitCodeBus();
        var value = zero ? 0u : 0x89ABCDEFu;
        ushort[] extension = sourceForm switch
        {
            0 => [0x8000], // Absolute short must sign extend.
            1 => [0, 0x6000],
            2 => [0xFFF0], // PC base is the extension word at $1002.
            3 => [0x12FC], // -4(PC,D1.W*2), D1.W=-2.
            _ => [(ushort)(value >> 16), (ushort)value]
        };
        M68kInterpreterTestHelpers.WriteWords(bus, 0x1000, new ushort[] { (ushort)(0x22B8 | sourceForm) }.Concat(extension).ToArray());
        var source = sourceForm switch { 0 => 0xFFFF8000u, 1 => 0x6000u, 2 => 0xFF2u, _ => 0xFFAu };
        if (sourceForm != 4) bus.WriteLong(source, value);
        bus.WriteLong(0x5000, 0xFFFFFFFF);
        bus.WriteLong(0x5004, 0x12345678);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.D[1] = 0x1234FFFE;
        cpu.State.A[1] = 0x5000;
        cpu.State.StatusRegister = 0x2713;
        cpu.ExecuteInstruction();
        Assert.Equal(value, bus.ReadLong(0x5000));
        Assert.Equal(0x12345678u, bus.ReadLong(0x5004));
        Assert.Equal(0x5000u, cpu.State.A[1]);
        Assert.Equal(0x1002u + 2u * (uint)extension.Length, cpu.State.ProgramCounter);
        Assert.Equal(zero ? 0x2714 : 0x2718, cpu.State.StatusRegister);
    }

    public static IEnumerable<object[]> PostIncrementMoves()
    {
        foreach (var model in new[] { M68kCpuModel.M68020, M68kCpuModel.M68EC020, M68kCpuModel.M68030 })
        foreach (var size in new[] { 1, 2, 4 })
        foreach (var sourceRegister in new[] { 2, 7 })
        foreach (var alias in new[] { false, true })
        foreach (var zero in new[] { false, true })
            yield return new object[] { model, size, sourceRegister, alias, zero };
    }

    [Theory]
    [MemberData(nameof(PostIncrementMoves))]
    public void MovePostIncrementResolvesAliasedDestinationAfterSourceUpdate(M68kCpuModel model, int size, int sourceRegister, bool alias, bool zero)
    {
        var bus = new Copper68kTestBus();
        var destinationRegister = alias ? sourceRegister : 0;
        var prefix = size == 1 ? 0x1000 : size == 2 ? 0x3000 : 0x2000;
        bus.WriteWords(0x1000, (ushort)(prefix | (destinationRegister << 9) | 0x98 | sourceRegister));
        bus.WriteLong(0x4000, zero ? 0u : 0x80010203u);
        bus.WriteLong(0x4004, 0x55667788);
        bus.WriteLong(0x4008, 0x99AABBCC);
        bus.WriteLong(0x5000, 0xCCDDEEFF);
        var expectedMemory = Enumerable.Range(0, 12).Select(i => 0x4000u + (uint)i)
            .Concat(Enumerable.Range(0, 4).Select(i => 0x5000u + (uint)i))
            .ToDictionary(address => address, address => (byte)(bus.ReadWord(address) >> 8));
        var increment = size == 1 && sourceRegister == 7 ? 2u : (uint)size;
        var destination = alias ? 0x4000u + increment : 0x5000u;
        var sourceBytes = Enumerable.Range(0, size).Select(i => expectedMemory[0x4000u + (uint)i]).ToArray();
        for (var i = 0; i < size; i++) expectedMemory[destination + (uint)i] = sourceBytes[i];
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[sourceRegister] = 0x4000;
        if (!alias) cpu.State.A[destinationRegister] = 0x5000;
        cpu.State.StatusRegister = 0x2713;
        cpu.ExecuteInstruction();
        var expectedValue = zero ? 0u : size == 1 ? 0x80u : size == 2 ? 0x8001u : 0x80010203u;
        var actual = bus.ReadLong(destination) >> (32 - size * 8);
        Assert.Equal(expectedValue, actual);
        Assert.Equal(0x4000u + increment, cpu.State.A[sourceRegister]);
        if (!alias) Assert.Equal(0x5000u, cpu.State.A[destinationRegister]);
        Assert.Equal(zero ? 0x2714 : 0x2718, cpu.State.StatusRegister);
        Assert.Equal(0x1002u, cpu.State.ProgramCounter);
        foreach (var pair in expectedMemory) Assert.Equal(pair.Value, (byte)(bus.ReadWord(pair.Key) >> 8));
    }

    public static IEnumerable<object[]> AbsoluteQuickSubtracts()
    {
        foreach (var model in new[] { M68kCpuModel.M68020, M68kCpuModel.M68EC020, M68kCpuModel.M68030 })
        foreach (var size in new[] { 1, 2, 4 })
        foreach (var immediate in new[] { 1, 8 })
        foreach (var arithmeticCase in new[] { 0, 1, 2 })
            yield return new object[] { model, size, immediate, arithmeticCase };
    }

    [Theory]
    [MemberData(nameof(AbsoluteQuickSubtracts))]
    public void SubtractQuickAbsolutePreservesWidthAndDecodesEight(M68kCpuModel model, int size, int immediate, int arithmeticCase)
    {
        var mask = size == 1 ? 0xFFu : size == 2 ? 0xFFFFu : uint.MaxValue;
        var signBit = (mask >> 1) + 1;
        var initial = arithmeticCase == 0 ? (uint)immediate : arithmeticCase == 1 ? 0u : signBit;
        var expected = arithmeticCase == 0 ? 0u : arithmeticCase == 1 ? mask - (uint)immediate + 1 : signBit - (uint)immediate;
        var shift = 32 - size * 8;
        var guard = size == 1 ? 0x00ABCDEFu : size == 2 ? 0x0000CDEFu : 0u;
        var bus = new Copper68kTestBus();
        var sizeBits = size == 1 ? 0 : size == 2 ? 1 : 2;
        bus.WriteWords(0x1000, (ushort)(0x5139 | ((immediate & 7) << 9) | (sizeBits << 6)), 0xC0, 0x4000);
        bus.WriteLong(0xC04000, (initial << shift) | guard);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.StatusRegister = 0x271F;
        cpu.ExecuteInstruction();
        Assert.Equal((expected << shift) | guard, bus.ReadLong(0xC04000));
        Assert.Equal(arithmeticCase == 0 ? 0x2704 : arithmeticCase == 1 ? 0x2719 : 0x2702, cpu.State.StatusRegister);
        Assert.Equal(0x1006u, cpu.State.ProgramCounter);
        Assert.Equal(0x7000u, cpu.State.A[7]);
    }

    public static IEnumerable<object[]> PostIncrementAdds()
    {
        foreach (var model in new[] { M68kCpuModel.M68020, M68kCpuModel.M68EC020, M68kCpuModel.M68030 })
        foreach (var size in new[] { 1, 2, 4 })
        foreach (var addressRegister in new[] { 2, 7 })
        foreach (var arithmeticCase in new[] { 0, 1, 2 })
            yield return new object[] { model, size, addressRegister, arithmeticCase };
    }

    [Theory]
    [MemberData(nameof(PostIncrementAdds))]
    public void AddToPostIncrementUpdatesFlagsMemoryAndAddressOnce(M68kCpuModel model, int size, int addressRegister, int arithmeticCase)
    {
        var mask = size == 1 ? 0xFFu : size == 2 ? 0xFFFFu : uint.MaxValue;
        var initial = arithmeticCase == 0 ? mask : arithmeticCase == 1 ? mask >> 1 : 5u;
        var expected = arithmeticCase == 0 ? 0u : arithmeticCase == 1 ? (mask >> 1) + 1 : 6u;
        var shift = 32 - size * 8;
        var guard = size == 1 ? 0x00ABCDEFu : size == 2 ? 0x0000CDEFu : 0u;
        var bus = new Copper68kTestBus();
        var sizeBits = size == 1 ? 0 : size == 2 ? 1 : 2;
        bus.WriteWords(0x1000, (ushort)(0xD518 | (sizeBits << 6) | addressRegister));
        bus.WriteLong(0x4000, (initial << shift) | guard);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x4000);
        cpu.State.A[addressRegister] = 0x4000;
        cpu.State.D[2] = 1;
        cpu.State.StatusRegister = 0x271F;
        cpu.ExecuteInstruction();
        Assert.Equal((expected << shift) | guard, bus.ReadLong(0x4000));
        Assert.Equal(0x4000u + (uint)(size == 1 && addressRegister == 7 ? 2 : size), cpu.State.A[addressRegister]);
        Assert.Equal(1u, cpu.State.D[2]);
        Assert.Equal(arithmeticCase == 0 ? 0x2715 : arithmeticCase == 1 ? 0x270A : 0x2700, cpu.State.StatusRegister);
        Assert.Equal(0x1002u, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(M68kCpuModel.M68020, 0x20, 0x1022u)]
    [InlineData(M68kCpuModel.M68EC020, 0x20, 0x1022u)]
    [InlineData(M68kCpuModel.M68030, 0x20, 0x1022u)]
    [InlineData(M68kCpuModel.M68020, 0xFFE0, 0x0FE2u)]
    [InlineData(M68kCpuModel.M68EC020, 0xFFE0, 0x0FE2u)]
    [InlineData(M68kCpuModel.M68030, 0xFFE0, 0x0FE2u)]
    public void PcRelativeJumpUsesSignedDisplacementFromExtensionWord(M68kCpuModel model, ushort displacement, uint target)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, 0x4EFA, displacement, 0x7001);
        bus.WriteWords(target, 0x7007);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.StatusRegister = 0x271F;
        cpu.ExecuteInstruction();
        Assert.Equal(target, cpu.State.ProgramCounter);
        Assert.Equal(0x7000u, cpu.State.A[7]);
        Assert.Equal(0x271F, cpu.State.StatusRegister);
        cpu.ExecuteInstruction();
        Assert.Equal(7u, cpu.State.D[0]);
    }

    public static IEnumerable<object[]> IndirectWordMoves()
    {
        foreach (var model in new[] { M68kCpuModel.M68020, M68kCpuModel.M68EC020, M68kCpuModel.M68030 })
        foreach (var value in new ushort[] { 0, 7, 0x8001 })
        foreach (var alias in new[] { false, true })
            yield return new object[] { model, value, alias };
    }

    [Theory]
    [MemberData(nameof(IndirectWordMoves))]
    public void MoveWordIndirectToIndirectCopiesOnlyWordAndPreservesAddressRegisters(
        M68kCpuModel model, ushort value, bool alias)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, alias ? (ushort)0x3090 : (ushort)0x3091);
        bus.WriteLong(0x4000, 0xAABBCCDD);
        bus.WriteWord(alias ? 0x4001u : 0x5000u, value);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[0] = 0x4001;
        cpu.State.A[1] = 0x5000;
        cpu.State.StatusRegister = 0x2713;
        cpu.ExecuteInstruction();
        Assert.Equal(0xAA0000DDu | ((uint)value << 8), bus.ReadLong(0x4000));
        Assert.Equal(0x4001u, cpu.State.A[0]);
        Assert.Equal(0x5000u, cpu.State.A[1]);
        Assert.Equal(value == 0 ? 0x2714 : (value & 0x8000) != 0 ? 0x2718 : 0x2710, cpu.State.StatusRegister);
        Assert.Equal(0x1002u, cpu.State.ProgramCounter);
    }
}
