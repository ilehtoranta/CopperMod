using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

internal static class Move16AlignmentBenchmark
{
    private const int WorkingSetSize = 1 << 20;
    private const int WorkingSetMask = WorkingSetSize - 16;
    private const int RegionSeparation = WorkingSetSize + 64;

    public static bool TryRun(string[] args)
    {
        if (!args.Contains("--move16-alignment", StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        var options = ParseOptions(args);
        Console.WriteLine(
            $"MOVE16 Vector128 alignment benchmark, warmup={options.Warmup}, " +
            $"measured={options.Iterations}, repeats={options.Repeats}, " +
            $"hardware-accelerated={Vector128.IsHardwareAccelerated}");
        Console.WriteLine(
            "source mod64\trepeat\titerations\tms\tcopies/sec\tGiB/sec\tallocated bytes\tchecksum\tdestination mod64");

        using var storage = new PinnedStorage();
        var buffersByAlignment = new[]
        {
            storage.CreateView(0),
            storage.CreateView(4),
            storage.CreateView(8),
            storage.CreateView(16),
            storage.CreateView(32),
            storage.CreateView(48)
        };
        foreach (var buffers in buffersByAlignment)
        {
            CopyMany(buffers, options.Warmup);
        }

        for (var repeat = 1; repeat <= options.Repeats; repeat++)
        {
            for (var alignmentOffset = 0; alignmentOffset < buffersByAlignment.Length; alignmentOffset++)
            {
                var buffers = buffersByAlignment[(alignmentOffset + repeat - 1) % buffersByAlignment.Length];
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                var started = Stopwatch.GetTimestamp();
                CopyMany(buffers, options.Iterations);
                var elapsed = Stopwatch.GetElapsedTime(started);
                var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                var checksum = ValidateAndChecksum(buffers);
                var copiesPerSecond = options.Iterations / elapsed.TotalSeconds;
                var gibPerSecond = copiesPerSecond * 16.0 / (1L << 30);
                Console.WriteLine(
                        $"{buffers.Alignment}\t{repeat}\t{options.Iterations}\t{elapsed.TotalMilliseconds:F3}\t" +
                        $"{copiesPerSecond:F0}\t{gibPerSecond:F3}\t{allocated}\t0x{checksum:X8}\t" +
                        $"{buffers.DestinationAddress & 63}");
            }
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CopyMany(AlignedBuffers buffers, int iterations)
    {
        ref var sourceBase = ref Unsafe.Add(
            ref MemoryMarshal.GetArrayDataReference(buffers.Storage),
            buffers.SourceOffset);
        ref var destinationBase = ref Unsafe.Add(
            ref MemoryMarshal.GetArrayDataReference(buffers.Storage),
            buffers.DestinationOffset);
        for (var index = 0; index < iterations; index++)
        {
            var offset = (index << 4) & WorkingSetMask;
            ref var source = ref Unsafe.Add(ref sourceBase, offset);
            ref var destination = ref Unsafe.Add(ref destinationBase, offset);
            var value = Unsafe.ReadUnaligned<Vector128<byte>>(ref source);
            Unsafe.WriteUnaligned(ref destination, value);
        }
    }

    private static uint ValidateAndChecksum(AlignedBuffers buffers)
    {
        var checksum = 2166136261u;
        for (var index = 0; index < WorkingSetSize; index++)
        {
            var expected = buffers.Storage[buffers.SourceOffset + index];
            var actual = buffers.Storage[buffers.DestinationOffset + index];
            if (actual != expected)
            {
                throw new InvalidOperationException(
                    $"Vector128 copy mismatch at offset {index}: expected {expected:X2}, actual {actual:X2}.");
            }

            checksum = unchecked((checksum ^ actual) * 16777619u);
        }

        return checksum;
    }

    private static Options ParseOptions(string[] args)
    {
        var warmup = 5_000_000;
        var iterations = 100_000_000;
        var repeats = 7;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--warmup":
                    warmup = ParseInt(args, ref index);
                    break;
                case "--instructions":
                    iterations = ParseInt(args, ref index);
                    break;
                case "--repeats":
                    repeats = ParseInt(args, ref index);
                    break;
            }
        }

        return new Options(warmup, iterations, repeats);
    }

    private static int ParseInt(string[] args, ref int index)
    {
        if (++index >= args.Length || !int.TryParse(args[index], out var value) || value <= 0)
        {
            throw new ArgumentException("Expected a positive integer benchmark argument.");
        }

        return value;
    }

    private sealed class PinnedStorage : IDisposable
    {
        private readonly GCHandle _storageHandle;
        private readonly int _alignedBaseOffset;
        private readonly nuint _alignedBaseAddress;

        public PinnedStorage()
        {
            Storage = GC.AllocateArray<byte>(RegionSeparation + WorkingSetSize + 63, pinned: true);
            _storageHandle = GCHandle.Alloc(Storage, GCHandleType.Pinned);
            var baseAddress = (nuint)_storageHandle.AddrOfPinnedObject();
            _alignedBaseOffset = 0;
            _alignedBaseAddress = baseAddress;
            for (var index = 0; index < Storage.Length; index++)
            {
                Storage[index] = unchecked((byte)((index * 37) + 11));
            }
        }

        public byte[] Storage { get; }

        public AlignedBuffers CreateView(int alignment)
        {
            var relativeOffset = CalculateOffset(_alignedBaseAddress, alignment, modulus: 64);
            var sourceOffset = _alignedBaseOffset + relativeOffset;
            return new AlignedBuffers(
                Storage,
                alignment,
                sourceOffset,
                sourceOffset + RegionSeparation,
                _alignedBaseAddress + (nuint)relativeOffset,
                _alignedBaseAddress + (nuint)(relativeOffset + RegionSeparation));
        }

        public void Dispose()
        {
            if (_storageHandle.IsAllocated)
            {
                _storageHandle.Free();
            }
        }

        private static int CalculateOffset(nuint address, int desiredModulo, int modulus)
            => (desiredModulo - (int)(address % (nuint)modulus) + modulus) % modulus;
    }

    private sealed record AlignedBuffers(
        byte[] Storage,
        int Alignment,
        int SourceOffset,
        int DestinationOffset,
        nuint SourceAddress,
        nuint DestinationAddress);

    private readonly record struct Options(int Warmup, int Iterations, int Repeats);
}
