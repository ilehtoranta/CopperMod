using System;
using System.Collections.Generic;

namespace CopperMod.Sid
{
    internal static class SidLoopDetector
    {
        private const int MaximumOccurrencesPerFingerprint = 64;
        private const ulong FnvaOffset1 = 14695981039346656037UL;
        private const ulong FnvaOffset2 = 1099511628211UL;
        private const ulong FnvaPrime1 = 1099511628211UL;
        private const ulong FnvaPrime2 = 14029467366897019727UL;
        private static readonly AudioRenderOptionsAdapter DetectionRenderOptions = new AudioRenderOptionsAdapter(44100, 1);

        public static SidLoopDetectionResult Detect(SidModule module, int subSongIndex, SidLoopDetectionOptions options)
        {
            ArgumentNullException.ThrowIfNull(module);
            ArgumentNullException.ThrowIfNull(options);

            if (subSongIndex < 0 || subSongIndex >= module.SubSongCount)
            {
                throw new ArgumentOutOfRangeException(nameof(subSongIndex), subSongIndex, "SID subtune index is outside the available range.");
            }

            var machine = new C64Machine(module);
            machine.Reset(subSongIndex);
            machine.Sid.ClearCapturedWrites();

            var cpuCyclesPerSecond = machine.Clock.CpuCyclesPerSecond;
            var maxSearchCycles = Math.Max(1, SidIntegerMath.TimeSpanToCycles(options.MaxSearchDuration, cpuCyclesPerSecond));
            var minimumStartCycles = SidIntegerMath.TimeSpanToCycles(options.MinimumStartTime, cpuCyclesPerSecond);
            var minimumLoopCycles = SidIntegerMath.TimeSpanToCycles(options.MinimumLoopDuration, cpuCyclesPerSecond);
            var history = new List<SidTickFingerprint>();
            var tickBoundaries = new List<long> { machine.Cycle };
            var occurrences = new Dictionary<SidTickFingerprint, List<int>>();
            var candidates = new List<SidLoopCandidate>();

            while (machine.Cycle < maxSearchCycles)
            {
                var tickStartCycle = machine.Cycle;
                var tickCycles = SidSong.GetCurrentTickCycleCount(module, subSongIndex, machine);
                machine.Sid.ClearCapturedWrites();
                machine.RenderFrame(Span<float>.Empty, DetectionRenderOptions, ReadOnlySpan<long>.Empty, tickCycles);
                machine.Sid.AdvanceTo(machine.Cycle);

                var fingerprint = CreateFingerprint(machine.SidWrites, tickStartCycle, tickCycles);
                history.Add(fingerprint);
                tickBoundaries.Add(machine.Cycle);
                var currentTick = history.Count - 1;

                if (TryAdvanceCandidates(candidates, history, tickBoundaries, cpuCyclesPerSecond, currentTick, out var result))
                {
                    return result!;
                }

                if (fingerprint.WriteCount > 0)
                {
                    AddCandidates(
                        candidates,
                        occurrences,
                        fingerprint,
                        currentTick,
                        tickBoundaries,
                        minimumStartCycles,
                        minimumLoopCycles,
                        options.MaximumActiveCandidates);
                }

                AddOccurrence(occurrences, fingerprint, currentTick);
            }

            return SidLoopDetectionResult.NotDetected(
                history.Count,
                SidIntegerMath.CyclesToTimeSpan(machine.Cycle, cpuCyclesPerSecond));
        }

        public static SidDurationDetectionResult DetectDuration(SidModule module, int subSongIndex, SidDurationDetectionOptions options)
        {
            ArgumentNullException.ThrowIfNull(module);
            ArgumentNullException.ThrowIfNull(options);

            if (subSongIndex < 0 || subSongIndex >= module.SubSongCount)
            {
                throw new ArgumentOutOfRangeException(nameof(subSongIndex), subSongIndex, "SID subtune index is outside the available range.");
            }

            var machine = new C64Machine(module);
            machine.Reset(subSongIndex);
            machine.Sid.ClearCapturedWrites();

            var cpuCyclesPerSecond = machine.Clock.CpuCyclesPerSecond;
            var maxSearchCycles = Math.Max(1, SidIntegerMath.TimeSpanToCycles(options.MaxSearchDuration, cpuCyclesPerSecond));
            var minimumStartCycles = SidIntegerMath.TimeSpanToCycles(options.MinimumStartTime, cpuCyclesPerSecond);
            var minimumLoopCycles = SidIntegerMath.TimeSpanToCycles(options.MinimumLoopDuration, cpuCyclesPerSecond);
            var silenceRequiredCycles = Math.Max(1, SidIntegerMath.TimeSpanToCycles(options.SilenceDuration, cpuCyclesPerSecond));
            var renderOptions = new AudioRenderOptionsAdapter(options.SampleRate, 1);
            var sampleClock = new SidSampleClock(cpuCyclesPerSecond, options.SampleRate, machine.Cycle);
            var sampleTargetCycles = Array.Empty<long>();
            var samples = Array.Empty<float>();
            var history = new List<SidTickFingerprint>();
            var tickBoundaries = new List<long> { machine.Cycle };
            var occurrences = new Dictionary<SidTickFingerprint, List<int>>();
            var candidates = new List<SidLoopCandidate>();
            long? silenceStartCycle = null;
            var becameActive = false;

            while (machine.Cycle < maxSearchCycles)
            {
                var tickStartCycle = machine.Cycle;
                var tickCycles = SidSong.GetCurrentTickCycleCount(module, subSongIndex, machine);
                var frames = sampleClock.PeekFrameCount(machine.Cycle, tickCycles);
                EnsureCapacity(ref sampleTargetCycles, frames);
                EnsureCapacity(ref samples, frames);
                var frameTargets = sampleTargetCycles.AsSpan(0, frames);
                _ = sampleClock.FillSampleTargets(machine.Cycle, tickCycles, frameTargets);

                machine.Sid.ClearCapturedWrites();
                machine.RenderFrame(samples.AsSpan(0, frames), renderOptions, frameTargets, tickCycles);
                machine.Sid.AdvanceTo(machine.Cycle);
                sampleClock.AdvanceFrames(frames);

                var fingerprint = CreateFingerprint(machine.SidWrites, tickStartCycle, tickCycles);
                history.Add(fingerprint);
                tickBoundaries.Add(machine.Cycle);
                var currentTick = history.Count - 1;

                if (TryAdvanceCandidates(candidates, history, tickBoundaries, cpuCyclesPerSecond, currentTick, out var loopResult))
                {
                    return SidDurationDetectionResult.FromLoop(loopResult!);
                }

                if (fingerprint.WriteCount > 0)
                {
                    AddCandidates(
                        candidates,
                        occurrences,
                        fingerprint,
                        currentTick,
                        tickBoundaries,
                        minimumStartCycles,
                        minimumLoopCycles,
                        options.MaximumActiveCandidates);
                }

                AddOccurrence(occurrences, fingerprint, currentTick);

                if (TryDetectSilence(
                    samples,
                    frames,
                    tickStartCycle,
                    machine.Cycle,
                    minimumStartCycles,
                    silenceRequiredCycles,
                    options.ActiveRangeThreshold,
                    options.SilenceRangeThreshold,
                    ref becameActive,
                    ref silenceStartCycle,
                    out var silenceCycle))
                {
                    return SidDurationDetectionResult.FromSilence(
                        SidIntegerMath.CyclesToTimeSpan(silenceCycle, cpuCyclesPerSecond),
                        SidIntegerMath.CyclesToTimeSpan(machine.Cycle - silenceCycle, cpuCyclesPerSecond),
                        history.Count,
                        SidIntegerMath.CyclesToTimeSpan(machine.Cycle, cpuCyclesPerSecond));
                }
            }

            return SidDurationDetectionResult.NotDetected(
                history.Count,
                SidIntegerMath.CyclesToTimeSpan(machine.Cycle, cpuCyclesPerSecond));
        }

        private static bool TryAdvanceCandidates(
            List<SidLoopCandidate> candidates,
            IReadOnlyList<SidTickFingerprint> history,
            IReadOnlyList<long> tickBoundaries,
            int cpuCyclesPerSecond,
            int currentTick,
            out SidLoopDetectionResult? result)
        {
            for (var i = candidates.Count - 1; i >= 0; i--)
            {
                var candidate = candidates[i];
                var expectedTick = candidate.StartTick + candidate.MatchedTicks;
                if (!history[currentTick].Equals(history[expectedTick]))
                {
                    candidates.RemoveAt(i);
                    continue;
                }

                candidate.MatchedTicks++;
                if (candidate.MatchedTicks < candidate.LengthTicks)
                {
                    continue;
                }

                var loopStart = SidIntegerMath.CyclesToTimeSpan(tickBoundaries[candidate.StartTick], cpuCyclesPerSecond);
                var loopEnd = SidIntegerMath.CyclesToTimeSpan(tickBoundaries[candidate.EndTick], cpuCyclesPerSecond);
                var searchDuration = SidIntegerMath.CyclesToTimeSpan(tickBoundaries[currentTick + 1], cpuCyclesPerSecond);
                result = SidLoopDetectionResult.DetectedLoop(
                    candidate.StartTick,
                    candidate.EndTick,
                    candidate.LengthTicks,
                    history.Count,
                    loopStart,
                    loopEnd,
                    searchDuration);
                return true;
            }

            result = null;
            return false;
        }

        private static void AddCandidates(
            List<SidLoopCandidate> candidates,
            IReadOnlyDictionary<SidTickFingerprint, List<int>> occurrences,
            SidTickFingerprint fingerprint,
            int currentTick,
            IReadOnlyList<long> tickBoundaries,
            long minimumStartCycles,
            long minimumLoopCycles,
            int maximumActiveCandidates)
        {
            if (!occurrences.TryGetValue(fingerprint, out var previousTicks))
            {
                return;
            }

            for (var i = 0; i < previousTicks.Count; i++)
            {
                var startTick = previousTicks[i];
                var lengthTicks = currentTick - startTick;
                if (lengthTicks <= 0)
                {
                    continue;
                }

                var startCycle = tickBoundaries[startTick];
                var endCycle = tickBoundaries[currentTick];
                if (startCycle < minimumStartCycles || endCycle - startCycle < minimumLoopCycles)
                {
                    continue;
                }

                candidates.Add(new SidLoopCandidate(startTick, currentTick, lengthTicks));
                if (candidates.Count > maximumActiveCandidates)
                {
                    candidates.RemoveAt(0);
                }
            }
        }

        private static void AddOccurrence(
            Dictionary<SidTickFingerprint, List<int>> occurrences,
            SidTickFingerprint fingerprint,
            int currentTick)
        {
            if (!occurrences.TryGetValue(fingerprint, out var ticks))
            {
                ticks = new List<int>();
                occurrences.Add(fingerprint, ticks);
            }

            ticks.Add(currentTick);
            if (ticks.Count > MaximumOccurrencesPerFingerprint)
            {
                ticks.RemoveAt(1);
            }
        }

        private static bool TryDetectSilence(
            float[] samples,
            int frames,
            long tickStartCycle,
            long tickEndCycle,
            long minimumStartCycles,
            long silenceRequiredCycles,
            double activeRangeThreshold,
            double silenceRangeThreshold,
            ref bool becameActive,
            ref long? silenceStartCycle,
            out long silenceCycle)
        {
            silenceCycle = 0;
            var range = MeasureRange(samples, frames);
            if (!becameActive)
            {
                if (range >= activeRangeThreshold)
                {
                    becameActive = true;
                }

                return false;
            }

            if (range > silenceRangeThreshold || tickStartCycle < minimumStartCycles)
            {
                silenceStartCycle = null;
                return false;
            }

            silenceStartCycle ??= tickStartCycle;
            if (tickEndCycle - silenceStartCycle.Value < silenceRequiredCycles)
            {
                return false;
            }

            silenceCycle = silenceStartCycle.Value;
            return true;
        }

        private static double MeasureRange(float[] samples, int frames)
        {
            if (frames <= 0)
            {
                return 0;
            }

            var min = samples[0];
            var max = samples[0];
            for (var i = 1; i < frames; i++)
            {
                var sample = samples[i];
                min = Math.Min(min, sample);
                max = Math.Max(max, sample);
            }

            return max - min;
        }

        private static void EnsureCapacity(ref long[] buffer, int length)
        {
            if (buffer.Length >= length)
            {
                return;
            }

            buffer = new long[Math.Max(length, Math.Max(1, buffer.Length * 2))];
        }

        private static void EnsureCapacity(ref float[] buffer, int length)
        {
            if (buffer.Length >= length)
            {
                return;
            }

            buffer = new float[Math.Max(length, Math.Max(1, buffer.Length * 2))];
        }

        private static SidTickFingerprint CreateFingerprint(IReadOnlyList<SidRegisterWrite> writes, long tickStartCycle, long tickCycles)
        {
            var hash1 = FnvaOffset1;
            var hash2 = FnvaOffset2;
            AddLong(ref hash1, ref hash2, tickCycles);
            AddInt(ref hash1, ref hash2, writes.Count);
            for (var i = 0; i < writes.Count; i++)
            {
                var write = writes[i];
                AddLong(ref hash1, ref hash2, write.Cycle - tickStartCycle);
                AddInt(ref hash1, ref hash2, write.ChipIndex);
                AddByte(ref hash1, ref hash2, write.Register);
                AddByte(ref hash1, ref hash2, write.Value);
            }

            return new SidTickFingerprint(Avalanche(hash1), Avalanche(hash2), writes.Count);
        }

        private static void AddInt(ref ulong hash1, ref ulong hash2, int value)
        {
            AddUInt(ref hash1, ref hash2, unchecked((uint)value));
        }

        private static void AddLong(ref ulong hash1, ref ulong hash2, long value)
        {
            AddULong(ref hash1, ref hash2, unchecked((ulong)value));
        }

        private static void AddUInt(ref ulong hash1, ref ulong hash2, uint value)
        {
            AddByte(ref hash1, ref hash2, (byte)value);
            AddByte(ref hash1, ref hash2, (byte)(value >> 8));
            AddByte(ref hash1, ref hash2, (byte)(value >> 16));
            AddByte(ref hash1, ref hash2, (byte)(value >> 24));
        }

        private static void AddULong(ref ulong hash1, ref ulong hash2, ulong value)
        {
            AddUInt(ref hash1, ref hash2, (uint)value);
            AddUInt(ref hash1, ref hash2, (uint)(value >> 32));
        }

        private static void AddByte(ref ulong hash1, ref ulong hash2, byte value)
        {
            hash1 = (hash1 ^ value) * FnvaPrime1;
            hash2 = (hash2 + value + 0x9E3779B97F4A7C15UL) * FnvaPrime2;
            hash2 ^= hash2 >> 29;
        }

        private static ulong Avalanche(ulong value)
        {
            value ^= value >> 33;
            value *= 0xff51afd7ed558ccdUL;
            value ^= value >> 33;
            value *= 0xc4ceb9fe1a85ec53UL;
            value ^= value >> 33;
            return value;
        }

        private sealed class SidLoopCandidate
        {
            public SidLoopCandidate(int startTick, int endTick, int lengthTicks)
            {
                StartTick = startTick;
                EndTick = endTick;
                LengthTicks = lengthTicks;
                MatchedTicks = 1;
            }

            public int StartTick { get; }

            public int EndTick { get; }

            public int LengthTicks { get; }

            public int MatchedTicks { get; set; }
        }

        private readonly struct SidTickFingerprint : IEquatable<SidTickFingerprint>
        {
            public SidTickFingerprint(ulong hash1, ulong hash2, int writeCount)
            {
                Hash1 = hash1;
                Hash2 = hash2;
                WriteCount = writeCount;
            }

            public ulong Hash1 { get; }

            public ulong Hash2 { get; }

            public int WriteCount { get; }

            public bool Equals(SidTickFingerprint other)
            {
                return Hash1 == other.Hash1 &&
                    Hash2 == other.Hash2 &&
                    WriteCount == other.WriteCount;
            }

            public override bool Equals(object? obj)
            {
                return obj is SidTickFingerprint other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = (int)Hash1;
                    hash = (hash * 397) ^ (int)(Hash1 >> 32);
                    hash = (hash * 397) ^ (int)Hash2;
                    hash = (hash * 397) ^ (int)(Hash2 >> 32);
                    hash = (hash * 397) ^ WriteCount;
                    return hash;
                }
            }
        }
    }
}
