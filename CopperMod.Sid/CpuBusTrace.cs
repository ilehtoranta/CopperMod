using System.Collections.Generic;
using Copper6510;

namespace CopperMod.Sid
{
    internal sealed class CpuBusTrace
    {
        private readonly List<CpuBusTraceFrame> _frames = new List<CpuBusTraceFrame>();

        public IReadOnlyList<CpuBusTraceFrame> Frames => _frames;

        public void Clear()
        {
            _frames.Clear();
        }

        internal void Add(CpuBusTraceFrame frame)
        {
            _frames.Add(frame);
        }
    }

    internal readonly struct CpuBusTraceFrame
    {
        public CpuBusTraceFrame(
            long requestedCycle,
            long cycle,
            int cycleOffset,
            byte opcode,
            ushort address,
            byte? value,
            Mos6510BusAccessKind kind,
            bool delayedByVic)
        {
            RequestedCycle = requestedCycle;
            Cycle = cycle;
            CycleOffset = cycleOffset;
            Opcode = opcode;
            Address = address;
            Value = value;
            Kind = kind;
            DelayedByVic = delayedByVic;
        }

        public long RequestedCycle { get; }

        public long Cycle { get; }

        public int CycleOffset { get; }

        public byte Opcode { get; }

        public ushort Address { get; }

        public byte? Value { get; }

        public Mos6510BusAccessKind Kind { get; }

        public bool DelayedByVic { get; }

        public long DelayCycles => Cycle - RequestedCycle;
    }
}
