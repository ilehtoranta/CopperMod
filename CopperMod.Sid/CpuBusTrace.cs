using System.Collections.Generic;

namespace CopperMod.Sid
{
    internal enum CpuBusAccessKind
    {
        OpcodeFetch,
        OperandFetch,
        Read,
        Write,
        DummyRead,
        DummyWrite,
        StackRead,
        StackWrite,
        VectorRead,
        Idle
    }

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
            CpuBusAccessKind kind,
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

        public CpuBusAccessKind Kind { get; }

        public bool DelayedByVic { get; }

        public long DelayCycles => Cycle - RequestedCycle;
    }
}
