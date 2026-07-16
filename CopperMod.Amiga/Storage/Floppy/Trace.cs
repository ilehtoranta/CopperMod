using System;

namespace CopperMod.Amiga
{
    internal enum AmigaDiskTraceEventKind
    {
        Unknown = 0,
        RegisterWrite,
        CiaBDriveControlWrite,
        DiskInputAdvance,
        WakeCandidate,
        DmaStarted,
        DmaWord,
        DmaCompleted,
        DmaCancelled,
        DmaStopped,
        DmaStartBlocked,
        DmaSyncMissing,
        DiskInterruptWrite
    }

    internal readonly struct AmigaDiskTraceCpuContext
    {
        public AmigaDiskTraceCpuContext(
            uint programCounter,
            uint lastInstructionProgramCounter,
            ushort lastOpcode,
            long cycles)
        {
            ProgramCounter = programCounter;
            LastInstructionProgramCounter = lastInstructionProgramCounter;
            LastOpcode = lastOpcode;
            Cycles = cycles;
        }

        public uint ProgramCounter { get; }

        public uint LastInstructionProgramCounter { get; }

        public ushort LastOpcode { get; }

        public long Cycles { get; }
    }

    internal readonly struct AmigaDiskTraceEvent
    {
        public AmigaDiskTraceEvent(
            long sequence,
            string backend,
            AmigaDiskTraceEventKind kind,
            long cycle,
            uint programCounter,
            uint lastInstructionProgramCounter,
            ushort lastOpcode,
            long cpuCycles,
            int beamLine,
            int beamHorizontal,
            ushort register,
            ushort value,
            ushort dmacon,
            ushort adkcon,
            ushort intena,
            ushort intreq,
            ushort dsklen,
            ushort dsksync,
            ushort dskbytr,
            ushort dskdatr,
            uint diskPointer,
            int selectedDrive,
            int activeDmaDrive,
            bool activeDma,
            int cylinder,
            int head,
            ushort pendingReadDmaWords,
            int requestedWords,
            int transferredWords,
            int sourceBit,
            int streamOffset,
            double streamPosition,
            long streamCycle,
            uint targetAddress,
            long targetCycle,
            long candidateCycle,
            long completionCycle,
            string detail)
        {
            Sequence = sequence;
            Backend = backend ?? string.Empty;
            Kind = kind;
            Cycle = cycle;
            ProgramCounter = programCounter;
            LastInstructionProgramCounter = lastInstructionProgramCounter;
            LastOpcode = lastOpcode;
            CpuCycles = cpuCycles;
            BeamLine = beamLine;
            BeamHorizontal = beamHorizontal;
            Register = register;
            Value = value;
            Dmacon = dmacon;
            Adkcon = adkcon;
            Intena = intena;
            Intreq = intreq;
            Dsklen = dsklen;
            Dsksync = dsksync;
            Dskbytr = dskbytr;
            Dskdatr = dskdatr;
            DiskPointer = diskPointer;
            SelectedDrive = selectedDrive;
            ActiveDmaDrive = activeDmaDrive;
            ActiveDma = activeDma;
            Cylinder = cylinder;
            Head = head;
            PendingReadDmaWords = pendingReadDmaWords;
            RequestedWords = requestedWords;
            TransferredWords = transferredWords;
            SourceBit = sourceBit;
            StreamOffset = streamOffset;
            StreamPosition = streamPosition;
            StreamCycle = streamCycle;
            TargetAddress = targetAddress;
            TargetCycle = targetCycle;
            CandidateCycle = candidateCycle;
            CompletionCycle = completionCycle;
            Detail = detail ?? string.Empty;
        }

        public long Sequence { get; }

        public string Backend { get; }

        public AmigaDiskTraceEventKind Kind { get; }

        public long Cycle { get; }

        public uint ProgramCounter { get; }

        public uint LastInstructionProgramCounter { get; }

        public ushort LastOpcode { get; }

        public long CpuCycles { get; }

        public int BeamLine { get; }

        public int BeamHorizontal { get; }

        public ushort Register { get; }

        public ushort Value { get; }

        public ushort Dmacon { get; }

        public ushort Adkcon { get; }

        public ushort Intena { get; }

        public ushort Intreq { get; }

        public ushort Dsklen { get; }

        public ushort Dsksync { get; }

        public ushort Dskbytr { get; }

        public ushort Dskdatr { get; }

        public uint DiskPointer { get; }

        public int SelectedDrive { get; }

        public int ActiveDmaDrive { get; }

        public bool ActiveDma { get; }

        public int Cylinder { get; }

        public int Head { get; }

        public ushort PendingReadDmaWords { get; }

        public int RequestedWords { get; }

        public int TransferredWords { get; }

        public int SourceBit { get; }

        public int StreamOffset { get; }

        public double StreamPosition { get; }

        public long StreamCycle { get; }

        public uint TargetAddress { get; }

        public long TargetCycle { get; }

        public long CandidateCycle { get; }

        public long CompletionCycle { get; }

        public string Detail { get; }
    }

    internal struct AmigaDiskTraceEventBuilder
    {
        public AmigaDiskTraceEventKind Kind;
        public long Cycle;
        public ushort Register;
        public ushort Value;
        public ushort Dmacon;
        public ushort Adkcon;
        public ushort Intena;
        public ushort Intreq;
        public ushort Dsklen;
        public ushort Dsksync;
        public ushort Dskbytr;
        public ushort Dskdatr;
        public uint DiskPointer;
        public int SelectedDrive;
        public int ActiveDmaDrive;
        public bool ActiveDma;
        public int Cylinder;
        public int Head;
        public ushort PendingReadDmaWords;
        public int RequestedWords;
        public int TransferredWords;
        public int SourceBit;
        public int StreamOffset;
        public double StreamPosition;
        public long StreamCycle;
        public uint TargetAddress;
        public long TargetCycle;
        public long CandidateCycle;
        public long CompletionCycle;
        public string Detail;
    }

    internal sealed class AmigaDiskTraceRecorder
    {
        private const int DefaultCapacity = 524288;
        private readonly AmigaDiskTraceEvent[] _events;
        private int _start;
        private int _count;
        private long _nextSequence;
        private string _backend = string.Empty;
        private Func<AmigaDiskTraceCpuContext>? _cpuContextProvider;

        public AmigaDiskTraceRecorder()
            : this(GetEnvironmentCapacity())
        {
        }

        public AmigaDiskTraceRecorder(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Trace capacity must be positive.");
            }

            _events = new AmigaDiskTraceEvent[capacity];
        }

        public int Count => _count;

        public void Configure(string backend, Func<AmigaDiskTraceCpuContext>? cpuContextProvider)
        {
            _backend = backend ?? string.Empty;
            _cpuContextProvider = cpuContextProvider;
        }

        public void Clear()
        {
            Array.Clear(_events);
            _start = 0;
            _count = 0;
            _nextSequence = 0;
        }

        public void Add(in AmigaDiskTraceEventBuilder builder)
        {
            var cpu = _cpuContextProvider?.Invoke() ?? default;
            var beam = AgnusPalBeamPosition.FromCycle(builder.Cycle);
            var entry = new AmigaDiskTraceEvent(
                _nextSequence++,
                _backend,
                builder.Kind,
                builder.Cycle,
                cpu.ProgramCounter,
                cpu.LastInstructionProgramCounter,
                cpu.LastOpcode,
                cpu.Cycles,
                beam.BeamLine,
                beam.BeamHorizontal,
                builder.Register,
                builder.Value,
                builder.Dmacon,
                builder.Adkcon,
                builder.Intena,
                builder.Intreq,
                builder.Dsklen,
                builder.Dsksync,
                builder.Dskbytr,
                builder.Dskdatr,
                builder.DiskPointer,
                builder.SelectedDrive,
                builder.ActiveDmaDrive,
                builder.ActiveDma,
                builder.Cylinder,
                builder.Head,
                builder.PendingReadDmaWords,
                builder.RequestedWords,
                builder.TransferredWords,
                builder.SourceBit,
                builder.StreamOffset,
                builder.StreamPosition,
                builder.StreamCycle,
                builder.TargetAddress,
                builder.TargetCycle,
                builder.CandidateCycle,
                builder.CompletionCycle,
                builder.Detail);
            var index = (_start + _count) % _events.Length;
            if (_count == _events.Length)
            {
                _events[_start] = entry;
                _start = (_start + 1) % _events.Length;
                return;
            }

            _events[index] = entry;
            _count++;
        }

        public AmigaDiskTraceEvent[] Capture()
        {
            if (_count == 0)
            {
                return Array.Empty<AmigaDiskTraceEvent>();
            }

            var result = new AmigaDiskTraceEvent[_count];
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = _events[(_start + i) % _events.Length];
            }

            return result;
        }

        public static bool IsEnvironmentEnabled()
        {
            var value = Environment.GetEnvironmentVariable("COPPER_DISK_DIVERGENCE_TRACE");
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
        }

        private static int GetEnvironmentCapacity()
        {
            var value = Environment.GetEnvironmentVariable("COPPER_DISK_DIVERGENCE_TRACE_CAPACITY");
            return int.TryParse(value, out var capacity) && capacity > 0
                ? capacity
                : DefaultCapacity;
        }
    }
}
