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
            long cycle,
            byte opcode,
            ushort? address,
            byte? value,
            Mos6510BusAccessKind? kind,
            bool cpuAdvanced = true,
            bool ready = true,
            bool busAvailable = true,
            bool vicOwned = false)
        {
            Cycle = cycle;
            Opcode = opcode;
            Address = address;
            Value = value;
            Kind = kind;
            CpuAdvanced = cpuAdvanced;
            Ready = ready;
            BusAvailable = busAvailable;
            VicOwned = vicOwned;
        }

        public long Cycle { get; }

        public byte Opcode { get; }

        public ushort? Address { get; }

        public byte? Value { get; }

        public Mos6510BusAccessKind? Kind { get; }

        public bool CpuAdvanced { get; }

        public bool Ready { get; }

        public bool BusAvailable { get; }

        public bool VicOwned { get; }
    }
}
