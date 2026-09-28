using CopperMod.Amiga.CopperStart.Graphics.Portable;
using Xunit.Abstractions;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsVectorAbiTests
{
    private const uint FallbackIdentity = 0xFA11_BA11;
    private readonly ITestOutputHelper _output;

    public NativeGraphicsVectorAbiTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void FixedImageRoutesEveryNegativeVectorFromTheRealLibraryBase()
    {
        // Keep synthetic images outside the bus's built-in Chip RAM decode.
        const uint libraryBase = 0x00A0_0000;
        const uint residentAddress = 0x00B0_0000;
        const uint codeAddress = 0x0084_0000;
        var program = CreateSyntheticProgram();
        var entries = program.Entries.ToDictionary(
            pair => pair.Key, pair => codeAddress + (uint)pair.Value);
        var image = NativeGraphicsLibraryImageBuilder.Build(
            libraryBase,
            residentAddress,
            libraryInitAddress: codeAddress + (uint)program.Fallback,
            entryPoints: entries,
            unimplementedEntry: codeAddress + (uint)program.Fallback);
        var bus = new AmigaBus();
        bus.MapWritableMemory(image.VectorBase, image.VectorBytes);
        bus.MapWritableMemory(image.LibraryBase, image.PositiveBytes);
        bus.MapWritableMemory(image.ResidentAddress, image.ResidentBytes);
        bus.MapWritableMemory(codeAddress, program.Code);

        VerifyEverySlot("fixed image", bus, libraryBase, residentAddress, codeAddress, program);
    }

    [Fact]
    public void RelocatedHunkRoutesEveryNegativeVectorFromTheRealLibraryBase()
    {
        var program = CreateSyntheticProgram();
        var hunk = NativeGraphicsLibraryHunkBuilder.Build(
            program.Code, program.Entries, program.Fallback);
        var bus = new AmigaBus();
        var addresses = new Queue<uint>(new[] { 0x0082_0000u, 0x00A4_0000u });
        var loader = new AmigaHunkProgramLoader(bus, size =>
        {
            var address = addresses.Dequeue();
            bus.MapWritableMemory(address, new byte[size]);
            return address;
        });
        var loaded = loader.Load(hunk.Bytes);
        Assert.Equal(2, loaded.SegmentBases.Count);
        Assert.Empty(addresses);

        var codeSegment = loaded.SegmentBases[0];
        var libraryBase = codeSegment + (uint)hunk.VectorOffset;
        var codeAddress = codeSegment + (uint)hunk.NativeCodeOffset;
        VerifyEverySlot("relocated HUNK", bus, libraryBase,
            loaded.SegmentBases[1], codeAddress, program);
    }

    private void VerifyEverySlot(
        string description, AmigaBus bus, uint libraryBase,
        uint residentAddress, uint codeAddress, SyntheticProgram program)
    {
        Assert.Equal((byte)0x80, bus.ReadByte(residentAddress + 0x0A));
        Assert.Equal((ushort)(-GraphicsLvoCatalog.LastPublicLvo),
            bus.ReadWord(libraryBase + 0x10));
        var initTable = bus.ReadLong(residentAddress + 0x16);
        var functionArray = bus.ReadLong(initTable + 4);
        var functionFailures = new List<string>();
        var vectorFailures = new List<string>();
        var publicSlots = 0;
        var reservedSlots = 0;
        var ordinal = 0;
        using var caller = new NativeCaller(bus, libraryBase);

        // The AUTOINIT function array has ordinal order -6, -12, ... .
        // Physical vectors instead live at LibraryBase plus the signed LVO.
        // No builder/scaffold vector-offset helper participates in this oracle.
        for (var displacement = -6; displacement >= GraphicsLvoCatalog.LastPublicLvo;
            displacement -= 6, ordinal++)
        {
            var isPublic = program.Entries.TryGetValue((GraphicsLvo)displacement, out var entryOffset);
            if (isPublic)
                publicSlots++;
            else
                reservedSlots++;
            var name = isPublic ? ((GraphicsLvo)displacement).ToString() : "reserved";
            var identity = isPublic ? PublicIdentity(displacement) : FallbackIdentity;
            var expectedEntry = codeAddress + (uint)(isPublic ? entryOffset : program.Fallback);

            var functionEntry = bus.ReadLong(functionArray + (uint)(ordinal * 4));
            var functionErrors = new List<string>();
            if (functionEntry != expectedEntry)
                functionErrors.Add($"pointer={functionEntry:X8}, expected={expectedEntry:X8}");
            AddCallErrors(functionErrors, caller.InvokeAbsolute(functionEntry),
                expectedEntry, identity, libraryBase);
            if (functionErrors.Count != 0)
                functionFailures.Add($"ordinal {ordinal}, LVO {displacement} ({name}): " +
                    string.Join("; ", functionErrors));

            var vectorAddress = checked((uint)((long)libraryBase + displacement));
            var opcode = bus.ReadWord(vectorAddress);
            var vectorEntry = bus.ReadLong(vectorAddress + 2);
            var vectorErrors = new List<string>();
            if (opcode != 0x4EF9)
                vectorErrors.Add($"opcode={opcode:X4}, expected=4EF9");
            if (vectorEntry != expectedEntry)
                vectorErrors.Add($"JMP target={vectorEntry:X8}, expected={expectedEntry:X8}");
            if (vectorEntry != functionEntry)
                vectorErrors.Add($"JMP target differs from AUTOINIT pointer={functionEntry:X8}");
            AddCallErrors(vectorErrors, caller.InvokeLibraryVector(displacement),
                vectorAddress, identity, libraryBase);
            if (vectorErrors.Count != 0)
                vectorFailures.Add($"LVO {displacement} ({name}) at {vectorAddress:X8}: " +
                    string.Join("; ", vectorErrors));
        }

        Assert.Equal(GraphicsLvoCatalog.PublicVectorCount, publicSlots);
        Assert.True(reservedSlots > 0);
        Assert.Equal(uint.MaxValue, bus.ReadLong(functionArray + (uint)(ordinal * 4)));
        _output.WriteLine($"{description}: slots={ordinal}, public={publicSlots}, reserved={reservedSlots}; " +
            $"AUTOINIT controls passed={ordinal - functionFailures.Count}; " +
            $"negative-vector calls passed={ordinal - vectorFailures.Count}.");
        Assert.True(functionFailures.Count == 0,
            $"AUTOINIT function-array controls failed ({functionFailures.Count}/{ordinal}); first eight:\n" +
            string.Join("\n", functionFailures.Take(8)));
        Assert.True(vectorFailures.Count == 0,
            $"{description}: {vectorFailures.Count}/{ordinal} negative-vector slots failed; first eight:\n" +
            string.Join("\n", vectorFailures.Take(8)));
    }

    private static void AddCallErrors(
        List<string> errors, CallObservation call, uint expectedFirstPc,
        uint expectedIdentity, uint libraryBase)
    {
        if (!call.Returned)
            errors.Add($"did not return: PC={call.Pc:X8}, SR={call.Sr:X4}");
        if (call.FirstPc != expectedFirstPc)
            errors.Add($"JSR destination={call.FirstPc:X8}, expected={expectedFirstPc:X8}");
        if (call.Value != expectedIdentity)
            errors.Add($"D0={call.Value:X8}, expected={expectedIdentity:X8}");
        if (call.LibraryBase != libraryBase)
            errors.Add($"A6={call.LibraryBase:X8}, expected={libraryBase:X8}");
        if (call.StackPointer != NativeCaller.InitialStackPointer + 4)
            errors.Add($"SP={call.StackPointer:X8}, expected={NativeCaller.InitialStackPointer + 4:X8}");
    }

    private static uint PublicIdentity(int displacement)
        => 0xC010_0000u | checked((uint)-displacement);

    private static SyntheticProgram CreateSyntheticProgram()
    {
        var vectors = Enum.GetValues<GraphicsLvo>().OrderByDescending(vector => (int)vector).ToArray();
        var code = new byte[(vectors.Length + 1) * 8];
        var entries = new Dictionary<GraphicsLvo, int>();
        for (var index = 0; index < vectors.Length; index++)
        {
            var offset = index * 8;
            entries.Add(vectors[index], offset);
            WriteIdentityStub(code, offset, PublicIdentity((int)vectors[index]));
        }

        var fallback = vectors.Length * 8;
        WriteIdentityStub(code, fallback, FallbackIdentity);
        return new SyntheticProgram(code, entries, fallback);
    }

    private static void WriteIdentityStub(byte[] code, int offset, uint identity)
    {
        BigEndian.WriteUInt16(code, offset, 0x203C); // MOVE.L #identity,D0
        BigEndian.WriteUInt32(code, offset + 2, identity);
        BigEndian.WriteUInt16(code, offset + 6, 0x4E75); // RTS
    }

    private sealed record SyntheticProgram(
        byte[] Code, IReadOnlyDictionary<GraphicsLvo, int> Entries, int Fallback);
    private readonly record struct CallObservation(
        bool Returned, uint Value, uint LibraryBase, uint StackPointer,
        uint FirstPc, uint Pc, ushort Sr);

    private sealed class NativeCaller : IDisposable
    {
        private const uint CallerAddress = 0x0045_0000;
        private const uint StackAddress = 0x00C7_0000;
        private const uint ReturnAddress = 0x00F7_0000;
        internal const uint InitialStackPointer = StackAddress + 0x200;
        private readonly AmigaBus _bus;
        private readonly uint _libraryBase;
        private readonly IM68kCore _cpu;

        internal NativeCaller(AmigaBus bus, uint libraryBase)
        {
            _bus = bus;
            _libraryBase = libraryBase;
            bus.MapWritableMemory(CallerAddress, new byte[8]);
            bus.MapWritableMemory(StackAddress, new byte[0x400]);
            _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
        }

        internal CallObservation InvokeLibraryVector(int displacement)
        {
            _bus.WriteWord(CallerAddress, 0x4EAE); // JSR d16(A6)
            _bus.WriteWord(CallerAddress + 2, unchecked((ushort)checked((short)displacement)));
            _bus.WriteWord(CallerAddress + 4, 0x4E75); // RTS to the fixture
            return Execute();
        }

        internal CallObservation InvokeAbsolute(uint target)
        {
            _bus.WriteWord(CallerAddress, 0x4EB9); // JSR absolute long
            _bus.WriteLong(CallerAddress + 2, target);
            _bus.WriteWord(CallerAddress + 6, 0x4E75); // RTS to the fixture
            return Execute();
        }

        private CallObservation Execute()
        {
            _bus.WriteLong(InitialStackPointer, ReturnAddress);
            _cpu.Reset(CallerAddress, InitialStackPointer);
            _cpu.State.D[0] = 0xDEAD_0000;
            _cpu.State.A[6] = _libraryBase;
            var firstPc = 0u;
            for (var instruction = 0; instruction < 32; instruction++)
            {
                _cpu.ExecuteInstruction();
                if (instruction == 0)
                    firstPc = _cpu.State.ProgramCounter;
                if (_cpu.State.ProgramCounter == ReturnAddress)
                    return Observe(returned: true, firstPc);
            }

            return Observe(returned: false, firstPc);
        }

        private CallObservation Observe(bool returned, uint firstPc)
            => new(returned, _cpu.State.D[0], _cpu.State.A[6], _cpu.State.A[7],
                firstPc, _cpu.State.ProgramCounter, _cpu.State.StatusRegister);

        public void Dispose() => _cpu.Dispose();
    }
}
