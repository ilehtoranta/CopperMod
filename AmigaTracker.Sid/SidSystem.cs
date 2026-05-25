using System;
using System.Collections.Generic;

namespace AmigaTracker.Sid
{
    internal sealed class SidSystem
    {
        private readonly List<SidRegisterWrite> _writes = new List<SidRegisterWrite>();
        private long _lastCycle;
        private double _sampleAccumulator;
        private long _sampleCycles;

        public SidSystem(IReadOnlyList<SidChipPlacement> placements, SidChipModel model)
        {
            if (placements is null || placements.Count == 0)
            {
                throw new ArgumentException("At least one SID chip placement is required.", nameof(placements));
            }

            Chips = new SidChip[placements.Count];
            for (var i = 0; i < placements.Count; i++)
            {
                Chips[i] = new SidChip(model, placements[i].BaseAddress);
            }
        }

        public SidChip[] Chips { get; }

        public IReadOnlyList<SidRegisterWrite> Writes => _writes;

        public void Reset()
        {
            _lastCycle = 0;
            _sampleAccumulator = 0;
            _sampleCycles = 0;
            _writes.Clear();
            foreach (var chip in Chips)
            {
                chip.Reset();
            }
        }

        public void ResetClock()
        {
            _lastCycle = 0;
            DiscardAccumulatedOutput();
        }

        public void DiscardAccumulatedOutput()
        {
            _sampleAccumulator = 0;
            _sampleCycles = 0;
        }

        public bool TryWrite(ushort address, byte value, long cycle)
        {
            for (var i = 0; i < Chips.Length; i++)
            {
                var chip = Chips[i];
                if (address < chip.BaseAddress || address >= chip.BaseAddress + 0x20)
                {
                    continue;
                }

                AdvanceTo(cycle);
                var register = (byte)(address - chip.BaseAddress);
                chip.Write(register, value);
                _writes.Add(new SidRegisterWrite(cycle, i, register, value));
                return true;
            }

            return false;
        }

        public float RenderSample(long cycle)
        {
            AdvanceTo(cycle);
            if (_sampleCycles == 0)
            {
                AccumulateOneCycle();
            }

            var sample = _sampleAccumulator / _sampleCycles;
            DiscardAccumulatedOutput();
            return (float)Math.Clamp(sample, -1.0, 1.0);
        }

        public void AdvanceTo(long targetCycle)
        {
            if (targetCycle <= _lastCycle)
            {
                return;
            }

            var cycles = targetCycle - _lastCycle;
            for (var i = 0; i < cycles; i++)
            {
                AccumulateOneCycle();
            }

            _lastCycle = targetCycle;
        }

        private void AccumulateOneCycle()
        {
            var sample = 0.0;
            foreach (var chip in Chips)
            {
                sample += chip.Render(1);
            }

            _sampleAccumulator += sample / Chips.Length;
            _sampleCycles++;
        }
    }
}
