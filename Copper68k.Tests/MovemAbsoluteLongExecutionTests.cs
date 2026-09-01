using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Copper68k;
using Xunit;

namespace Copper68k.Tests;

/// <summary>Public-core regression for the native DOS register-save instruction.</summary>
public sealed class MovemAbsoluteLongExecutionTests
{
    private const uint InstructionAddress = 0x0060_1148;
    private const uint DestinationAddress = 0x0060_00D8;
    private const uint StackPointer = 0x00EF_FFF0;

    public static IEnumerable<object[]> Cases()
    {
        foreach (var cpu in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020, M68kCpuModel.M68040 })
        foreach (var mask in new ushort[] { 0x7FFF, 0x8081, 0x0000 })
        foreach (var absoluteLong in new[] { true, false })
            yield return new object[] { cpu, mask, absoluteLong };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void StoresSelectedRegistersInOrderWithoutChangingRegisterState(
        M68kCpuModel model, ushort mask, bool absoluteLong)
    {
        // Motorola M68000PRM, MOVEM pp.4-128..130: control-mode order D0..D7,
        // A0..A7; unchanged condition codes; absolute-long mode is 111/001.
        // https://www.nxp.com/docs/en/reference-manual/M68000PRM.pdf
        var instruction = absoluteLong
            ? new byte[] { 0x48, 0xF9, (byte)(mask >> 8), (byte)mask, 0x00, 0x60, 0x00, 0xD8 }
            : new byte[] { 0x48, 0xD3, (byte)(mask >> 8), (byte)mask }; // Same operation through (A3).
        var bus = new MovemBus();
        bus.Seed(InstructionAddress, instruction);
        // These NOPs are fetch padding only; exactly one instruction is executed.
        bus.Seed(InstructionAddress + (uint)instruction.Length,
            new byte[] { 0x4E, 0x71, 0x4E, 0x71, 0x4E, 0x71, 0x4E, 0x71 });
        var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(InstructionAddress, StackPointer);
        for (var register = 0; register < 8; register++)
        {
            cpu.State.D[register] = 0x0102_0304u + (uint)register * 0x1010_1010u;
            if (register < 7)
                cpu.State.A[register] = 0x8182_8384u + (uint)register * 0x1010_1010u;
        }
        if (!absoluteLong) cpu.State.A[3] = DestinationAddress;
        cpu.State.StatusRegister = 0x271F;
        var beforeD = cpu.State.D.ToArray();
        var beforeA = cpu.State.A.ToArray();
        var beforeSr = cpu.State.StatusRegister;
        var expectedMemory = bus.SnapshotMemory();
        var expectedStack = bus.SnapshotStack();
        var expectedValues = beforeD.Concat(beforeA)
            .Where((_, register) => (mask & (1 << register)) != 0).ToArray();
        var expectedBytes = new byte[expectedValues.Length * 4];
        for (var index = 0; index < expectedValues.Length; index++)
            BinaryPrimitives.WriteUInt32BigEndian(expectedBytes.AsSpan(index * 4, 4), expectedValues[index]);
        expectedBytes.CopyTo(expectedMemory.AsSpan((int)(DestinationAddress - MovemBus.MemoryBase)));
        var assembly = typeof(M68kCoreFactory).Assembly;
        var actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))).ToLowerInvariant();
        var expectedHash = Environment.GetEnvironmentVariable("COPPER68K_MOVEM_EXPECTED_SHA256");
        var passed = false;
        var executionStarted = false;
        var executionReturned = false;
        string? error = null;
        bus.ClearTrace();
        try
        {
            if (expectedHash is not null) Assert.Equal(expectedHash, actualHash);
            executionStarted = true;
            cpu.ExecuteInstruction();
            executionReturned = true;
            Assert.Equal(InstructionAddress + (uint)instruction.Length, cpu.State.ProgramCounter);
            Assert.Equal(beforeD, cpu.State.D);
            Assert.Equal(beforeA, cpu.State.A);
            Assert.Equal(beforeSr, cpu.State.StatusRegister);
            Assert.False(cpu.State.Halted);
            Assert.False(cpu.State.Stopped);
            Assert.Equal(expectedMemory, bus.SnapshotMemory());
            Assert.Equal(expectedStack, bus.SnapshotStack());
            Assert.Equal(Enumerable.Range(0, expectedBytes.Length).Select(index => DestinationAddress + (uint)index),
                bus.Writes.SelectMany(write => Enumerable.Range(0, write.Bytes).Select(index => write.Address + (uint)index)));
            Assert.All(bus.Writes, write => Assert.Equal(M68kBusAccessKind.CpuDataWrite, write.Kind));
            Assert.Equal(expectedBytes, bus.Writes.SelectMany(write => write.ValueBytes).ToArray());
            if (model == M68kCpuModel.M68020 && absoluteLong)
            {
                // MC68020UM §8.2.7 p.8-29: cache case4+3n; its immediate-address
                // footnote uses a WORD register mask (§8.2 p.8-10), not long data.
                // §8.2.4 p.8-17 adds4 for #word,xxx.L. This zero-wait core model
                // assertion does not qualify cache misses, arbitration or hardware timing.
                // https://www.nxp.com/docs/en/data-sheet/MC68020UM.pdf
                Assert.Equal(8L + 3L * expectedValues.Length, cpu.State.NativeCycles);
            }
            passed = true;
        }
        catch (Exception exception)
        {
            error = exception.ToString();
            throw;
        }
        finally
        {
            var directory = Environment.GetEnvironmentVariable("COPPER68K_MOVEM_ARTIFACTS");
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
                var report = new
                {
                    passed, model = model.ToString(), mask = $"{mask:X4}",
                    addressing = absoluteLong ? "absolute-long" : "address-indirect-control",
                    instructionAddress = $"{InstructionAddress:X8}", destinationAddress = $"{DestinationAddress:X8}",
                    instructionHex = Convert.ToHexString(instruction), executionStarted, executionReturned, error,
                    cpuAssembly = new { path = assembly.Location, sha256 = actualHash,
                        mvid = assembly.ManifestModule.ModuleVersionId, expectedSha256 = expectedHash },
                    before = new { d = beforeD, a = beforeA, sr = beforeSr, pc = InstructionAddress, sp = StackPointer },
                    after = new { d = cpu.State.D, a = cpu.State.A, sr = cpu.State.StatusRegister,
                        pc = cpu.State.ProgramCounter, sp = cpu.State.A[7], cpu.State.NativeCycles, cpu.State.Cycles,
                        cpu.State.LastOpcode, cpu.State.LastInstructionProgramCounter, cpu.State.Halted, cpu.State.Stopped },
                    expectedValues, expectedBytes = Convert.ToHexString(expectedBytes),
                    expectedPc = InstructionAddress + (uint)instruction.Length,
                    expectedNativeCycles = model == M68kCpuModel.M68020 && absoluteLong
                        ? (long?)(8 + 3 * expectedValues.Length) : null,
                    memoryMatches = expectedMemory.SequenceEqual(bus.SnapshotMemory()),
                    stackUnchanged = expectedStack.SequenceEqual(bus.SnapshotStack()),
                    registersUnchanged = beforeD.SequenceEqual(cpu.State.D) && beforeA.SequenceEqual(cpu.State.A),
                    writes = bus.Writes, reads = bus.Reads,
                    timingScope = "zero-wait cache-case model only for 68020 absolute-long; 000/040 timing observed only",
                    instructionCount = executionReturned ? 1 : 0,
                    hostGateways = 0,
                };
                var path = Path.Combine(directory, $"{model}-{mask:X4}-{(absoluteLong ? "absolute" : "indirect")}.json");
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                JsonSerializer.Serialize(stream, report, new JsonSerializerOptions { WriteIndented = true });
            }
        }
    }

    private sealed record Transfer(uint Address, int Bytes, uint Value, M68kBusAccessKind Kind)
    {
        public byte[] ValueBytes => Enumerable.Range(0, Bytes)
            .Select(index => (byte)(Value >> (8 * (Bytes - index - 1)))).ToArray();
    }

    private sealed class MovemBus : IM68kBus
    {
        public const uint MemoryBase = 0x0060_0000;
        private const uint StackBase = StackPointer - 64;
        private readonly byte[] _memory = Enumerable.Repeat((byte)0xA5, 0x2000).ToArray();
        private readonly byte[] _stack = Enumerable.Repeat((byte)0x5A, 128).ToArray();
        public List<Transfer> Writes { get; } = new();
        public List<Transfer> Reads { get; } = new();

        public byte[] SnapshotMemory() => _memory.ToArray();
        public byte[] SnapshotStack() => _stack.ToArray();
        public void Seed(uint address, byte[] bytes) => bytes.CopyTo(Span(address, bytes.Length));
        public void ClearTrace() { Writes.Clear(); Reads.Clear(); }

        public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind accessKind)
            => (byte)Read(address, 1, accessKind);
        public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind accessKind)
            => (ushort)Read(address, 2, accessKind);
        public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind accessKind)
            => Read(address, 4, accessKind);
        public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind accessKind)
            => Write(address, 1, value, accessKind);
        public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind accessKind)
            => Write(address, 2, value, accessKind);
        public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind accessKind)
            => Write(address, 4, value, accessKind);
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
            if (address >= MemoryBase && (ulong)address + (uint)bytes <= (ulong)MemoryBase + (uint)_memory.Length)
                return _memory.AsSpan((int)(address - MemoryBase), bytes);
            if (address >= StackBase && (ulong)address + (uint)bytes <= (ulong)StackBase + (uint)_stack.Length)
                return _stack.AsSpan((int)(address - StackBase), bytes);
            throw new InvalidOperationException($"Unmapped CPU access ${address:X8}+{bytes}.");
        }
    }
}
