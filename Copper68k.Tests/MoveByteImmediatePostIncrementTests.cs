using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Copper68k;
using Xunit;

namespace Copper68k.Tests;

/// <summary>Public-core regression for the byte append used by the original Rename command.</summary>
public sealed class MoveByteImmediatePostIncrementTests
{
    private const uint InstructionAddress = 0x0010_026A;
    private const uint InterruptStack = 0x0010_0C00;
    private const uint UserStack = 0x0010_0E00;
    private const uint MasterStack = 0x0010_1000;

    public static IEnumerable<object[]> Cases()
    {
        foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68010,
                     M68kCpuModel.M68020, M68kCpuModel.M68030, M68kCpuModel.M68040 })
        foreach (var register in Enumerable.Range(0, 8))
        foreach (var extension in new ushort[] { 0xCA00, 0x002F, 0x5580 })
        {
            yield return new object[] { model, register, extension, "interrupt" };
            if (register != 7) continue;
            yield return new object[] { model, register, extension, "user" };
            if (model is M68kCpuModel.M68020 or M68kCpuModel.M68030 or M68kCpuModel.M68040)
                yield return new object[] { model, register, extension, "master" };
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void AppendsOneByteAndUpdatesOnlyDestinationAndMoveFlags(
        M68kCpuModel model, int register, ushort extension, string stackBank)
    {
        // M68000PRM, MOVE pp.4-116..118 and postincrement p.2-6: byte immediate
        // uses the low extension byte; X is unchanged, V/C clear, N/Z follow it.
        // A7 advances by two for a byte, while A0..A6 advance by one.
        // https://www.nxp.com/docs/en/reference-manual/M68000PRM.pdf
        var opcode = (ushort)(0x10FC | register << 9);
        var instruction = new byte[] { (byte)(opcode >> 8), (byte)opcode,
            (byte)(extension >> 8), (byte)extension };
        var bus = new ByteMoveBus();
        bus.Seed(InstructionAddress, instruction);
        bus.Seed(InstructionAddress + 4, new byte[] { 0x4E, 0x71, 0x4E, 0x71, 0x4E, 0x71, 0x4E, 0x71 });
        var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(InstructionAddress, InterruptStack);
        cpu.State.ResetStackPointers(InterruptStack, UserStack, supervisorMode: true);
        cpu.State.SetMasterStackPointer(MasterStack);
        for (var index = 0; index < 8; index++)
        {
            cpu.State.D[index] = 0x0123_4567u + (uint)index * 0x1010_1010u;
            if (index < 7) cpu.State.A[index] = 0x0020_0000u + (uint)index * 0x100u;
        }
        var preservedSr = (ushort)(stackBank switch
        {
            "user" => 0x0500,
            "master" => 0x3500,
            _ => 0x2500,
        });
        var value = (byte)extension;
        var expectedCondition = extension switch
        {
            0xCA00 => M68kCpuState.Zero | M68kCpuState.Extend,
            0x002F => 0,
            0x5580 => M68kCpuState.Negative | M68kCpuState.Extend,
            _ => throw new InvalidOperationException("Unknown independent test vector."),
        };
        // Start N/Z opposite to their required outcome, and V/C set.
        var initialCondition = ((~expectedCondition) & (M68kCpuState.Negative | M68kCpuState.Zero))
            | (expectedCondition & M68kCpuState.Extend) | M68kCpuState.Carry | M68kCpuState.Overflow;
        cpu.State.StatusRegister = (ushort)(preservedSr | initialCondition);
        if (register != 7)
            cpu.State.A[register] = ByteMoveBus.MemoryBase + 0x801u + (uint)register * 0x10u;
        var destination = cpu.State.A[register];
        var expectedD = cpu.State.D.ToArray();
        var expectedA = cpu.State.A.ToArray();
        expectedA[register] += register == 7 ? 2u : 1u;
        var before = Snapshot(cpu.State);
        var expectedMemory = bus.SnapshotMemory();
        expectedMemory[(int)(destination - ByteMoveBus.MemoryBase)] = value;
        var expectedUserStack = cpu.State.UserStackPointer + (register == 7 && stackBank == "user" ? 2u : 0u);
        var expectedInterruptStack = cpu.State.InterruptStackPointer + (register == 7 && stackBank == "interrupt" ? 2u : 0u);
        var expectedMasterStack = cpu.State.MasterStackPointer + (register == 7 && stackBank == "master" ? 2u : 0u);
        var assembly = typeof(M68kCoreFactory).Assembly;
        var actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))).ToLowerInvariant();
        var expectedHash = Environment.GetEnvironmentVariable("COPPER68K_BYTE_POST_EXPECTED_SHA256");
        var executionStarted = false;
        var executionReturned = false;
        var passed = false;
        string? error = null;
        bus.ClearTrace();
        try
        {
            if (expectedHash is not null) Assert.Equal(expectedHash, actualHash);
            executionStarted = true;
            cpu.ExecuteInstruction();
            executionReturned = true;
            Assert.Equal(InstructionAddress + 4, cpu.State.ProgramCounter);
            Assert.Equal(opcode, cpu.State.LastOpcode);
            Assert.Equal(InstructionAddress, cpu.State.LastInstructionProgramCounter);
            Assert.Equal(expectedD, cpu.State.D);
            Assert.Equal(expectedA, cpu.State.A);
            Assert.Equal((ushort)(preservedSr | expectedCondition), cpu.State.StatusRegister);
            Assert.Equal(expectedUserStack, cpu.State.UserStackPointer);
            Assert.Equal(expectedInterruptStack, cpu.State.InterruptStackPointer);
            Assert.Equal(expectedMasterStack, cpu.State.MasterStackPointer);
            Assert.False(cpu.State.Halted);
            Assert.False(cpu.State.Stopped);
            Assert.Equal(expectedMemory, bus.SnapshotMemory());
            var write = Assert.Single(bus.Writes);
            Assert.Equal(new Transfer(destination, 1, value, M68kBusAccessKind.CpuDataWrite), write);
            if (model == M68kCpuModel.M68020)
            {
                // MC68020UM §8.2.6 p.8-23: cache case #<data>.B,W to (An)+ is
                // six clocks. This zero-wait fixture does not model cache misses,
                // instruction-stream overlap or Amiga bus arbitration.
                // https://www.nxp.com/docs/en/data-sheet/MC68020UM.pdf
                Assert.Equal(6L, cpu.State.NativeCycles);
            }
            if (model == M68kCpuModel.M68040)
                Assert.Equal(1L, cpu.State.NativeCycles); // Configured approximate profile.
            passed = true;
        }
        catch (Exception exception)
        {
            error = exception.ToString();
            throw;
        }
        finally
        {
            var directory = Environment.GetEnvironmentVariable("COPPER68K_BYTE_POST_ARTIFACTS");
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
                var report = new
                {
                    passed, model = model.ToString(), register, stackBank,
                    instructionHex = Convert.ToHexString(instruction), destination,
                    executionStarted, executionReturned, error,
                    cpuAssembly = new { path = assembly.Location, sha256 = actualHash,
                        mvid = assembly.ManifestModule.ModuleVersionId, expectedSha256 = expectedHash },
                    before, after = Snapshot(cpu.State),
                    expected = new { d = expectedD, a = expectedA, sr = (ushort)(preservedSr | expectedCondition),
                        pc = InstructionAddress + 4, userStack = expectedUserStack,
                        interruptStack = expectedInterruptStack, masterStack = expectedMasterStack,
                        memorySha256 = Convert.ToHexString(SHA256.HashData(expectedMemory)).ToLowerInvariant() },
                    memorySha256 = Convert.ToHexString(SHA256.HashData(bus.SnapshotMemory())).ToLowerInvariant(),
                    writes = bus.Writes, reads = bus.Reads,
                };
                File.WriteAllText(Path.Combine(directory, $"{model}-A{register}-{stackBank}-{extension:X4}.json"),
                    JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
    }

    private static object Snapshot(M68kCpuState state) => new
    {
        d = state.D.ToArray(), a = state.A.ToArray(), pc = state.ProgramCounter, sr = state.StatusRegister,
        userStack = state.UserStackPointer, interruptStack = state.InterruptStackPointer,
        masterStack = state.MasterStackPointer, cycles = state.Cycles, nativeCycles = state.NativeCycles,
        lastOpcode = state.LastOpcode, lastInstructionPc = state.LastInstructionProgramCounter,
        halted = state.Halted, stopped = state.Stopped,
    };

    private readonly record struct Transfer(uint Address, int Bytes, uint Value, M68kBusAccessKind Kind);

    private sealed class ByteMoveBus : IM68kBus
    {
        public const uint MemoryBase = 0x0010_0000;
        private readonly byte[] _memory = Enumerable.Repeat((byte)0xA5, 0x2000).ToArray();
        public List<Transfer> Writes { get; } = new();
        public List<Transfer> Reads { get; } = new();
        public byte[] SnapshotMemory() => _memory.ToArray();
        public void Seed(uint address, byte[] bytes) => bytes.CopyTo(Span(address, bytes.Length));
        public void ClearTrace() { Writes.Clear(); Reads.Clear(); }
        public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind kind) => (byte)Read(address, 1, kind);
        public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind kind) => (ushort)Read(address, 2, kind);
        public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind kind) => Read(address, 4, kind);
        public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind kind) => Write(address, 1, value, kind);
        public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind kind) => Write(address, 2, value, kind);
        public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind kind) => Write(address, 4, value, kind);
        public void ResetExternalDevices(long cycle) => throw new InvalidOperationException("Unexpected RESET.");

        private uint Read(uint address, int bytes, M68kBusAccessKind kind)
        {
            var data = Span(address, bytes);
            var value = bytes switch
            {
                1 => data[0], 2 => BinaryPrimitives.ReadUInt16BigEndian(data),
                4 => BinaryPrimitives.ReadUInt32BigEndian(data), _ => throw new InvalidOperationException(),
            };
            Reads.Add(new(address, bytes, value, kind));
            return value;
        }

        private void Write(uint address, int bytes, uint value, M68kBusAccessKind kind)
        {
            var data = Span(address, bytes);
            for (var index = 0; index < bytes; index++) data[index] = (byte)(value >> (8 * (bytes - index - 1)));
            Writes.Add(new(address, bytes, value, kind));
        }

        private Span<byte> Span(uint address, int bytes)
        {
            if (address < MemoryBase || (ulong)address + (uint)bytes > (ulong)MemoryBase + (uint)_memory.Length)
                throw new InvalidOperationException($"Unmapped CPU access ${address:X8}+{bytes}.");
            return _memory.AsSpan((int)(address - MemoryBase), bytes);
        }
    }
}
