using System;
using System.Collections.Generic;
using System.Diagnostics;
using CopperMod.Abstractions;

namespace CopperMod.Cust
{
    internal sealed class CustMachine
    {
        private readonly HunkFile _hunk;
        private readonly DeliTagTable _rawTags;
        private readonly List<ModuleDiagnostic> _diagnostics = new List<ModuleDiagnostic>();
        private readonly uint[] _segmentBases;
        private readonly int _listDataSegmentIndex;
        private readonly uint _hostGetListDataAddress = CustConstants.HostCallbackBaseAddress;
        private readonly uint _hostOkAddress = CustConstants.HostCallbackBaseAddress + 0x10;
        private readonly uint _hostSongEndAddress = CustConstants.HostCallbackBaseAddress + 0x20;
        private IReadOnlyDictionary<uint, uint> _tags = new Dictionary<uint, uint>();
        private int _subSongCount = 1;
        private int _firstSubSongNumber;
        private int _currentSubSongIndex;
        private bool _songEnded;

        public CustMachine(HunkFile hunk, DeliTagTable tags)
            : this(hunk, tags, M68kCoreFactory.Default, M68kBackendKind.AccurateM68000)
        {
        }

        public CustMachine(HunkFile hunk, DeliTagTable tags, IM68kCoreFactory cpuFactory, M68kBackendKind cpuBackend)
        {
            _hunk = hunk ?? throw new ArgumentNullException(nameof(hunk));
            _rawTags = tags ?? throw new ArgumentNullException(nameof(tags));
            _segmentBases = new uint[hunk.Segments.Count];
            _listDataSegmentIndex = ResolveListDataSegmentIndex(hunk, tags);
            Bus = new AmigaBus();
            Cpu = (cpuFactory ?? throw new ArgumentNullException(nameof(cpuFactory))).Create(cpuBackend, Bus);
            Reset(0);
        }

        public AmigaBus Bus { get; }

        public IM68kCore Cpu { get; }

        public IReadOnlyList<ModuleDiagnostic> Diagnostics => _diagnostics;

        public IReadOnlyList<CustomRegisterWrite> CustomRegisterWrites => Bus.CustomRegisterWrites;

        public int SubSongCount => _subSongCount;

        public int CurrentSubSongIndex => _currentSubSongIndex;

        public bool SongEnded => _songEnded;

        public bool AudioFilterEnabled => Bus.AudioFilterEnabled;

        public ModuleChannelWaveform? LastChannelWaveform { get; private set; }

        public long QuantumCycleCount { get; private set; } = (long)Math.Round(CustConstants.A500PalCpuClockHz / CustConstants.A500PalVBlankHz);

        public void Reset(int subSongIndex)
        {
            _currentSubSongIndex = Math.Max(0, subSongIndex);
            _songEnded = false;
            LastChannelWaveform = null;
            _diagnostics.Clear();
            Bus.Reset();
            AllocateSegments();
            LoadSegments();
            ApplyRelocations();
            _tags = BuildAbsoluteTags();
            InstallHostEnvironment();
            Cpu.Reset(0, CustConstants.StackTopAddress);
            CallIfPresent(CustConstants.DtpInitPlayer);
            UpdateSubSongRange();
            WriteHostWord(CustConstants.DtgSoundNumberOffset, (ushort)(_firstSubSongNumber + _currentSubSongIndex));
            CallIfPresent(CustConstants.DtpInitSound);
            Bus.Paula.AdvanceTo(Cpu.State.Cycles);
        }

        public void SelectSubSong(int index)
        {
            if (index < 0 || index >= _subSongCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "CUST subsong index is outside the available range.");
            }

            Reset(index);
        }

        public void End()
        {
            CallIfPresent(CustConstants.DtpEndSound);
            CallIfPresent(CustConstants.DtpEndPlayer);
        }

        public void RenderQuantum(Span<float> destination, int frames, int channels, int sampleRate, bool captureChannels)
        {
            if (frames <= 0)
            {
                return;
            }

            var startCycle = Cpu.State.Cycles;
            var endCycle = startCycle + QuantumCycleCount;
            if (captureChannels)
            {
                Bus.Paula.BeginChannelCapture(frames, sampleRate);
            }

            CallIfPresent(CustConstants.DtpInterrupt, QuantumCycleCount, advancePaulaAtEnd: false);
            var cyclesPerOutputFrame = QuantumCycleCount / (double)frames;
            for (var frame = 0; frame < frames; frame++)
            {
                var targetCycle = startCycle + (long)Math.Round((frame + 1) * cyclesPerOutputFrame);
                Bus.Paula.RenderSample(targetCycle, destination, frame, channels);
            }

            Bus.Paula.AdvanceTo(endCycle);
            if (Cpu.State.Cycles < endCycle)
            {
                Cpu.State.Cycles = endCycle;
            }

            LastChannelWaveform = captureChannels ? Bus.Paula.FinishChannelCapture() : null;
        }

        private void AllocateSegments()
        {
            var next = CustConstants.DefaultModuleBaseAddress;
            for (var i = 0; i < _hunk.Segments.Count; i++)
            {
                _segmentBases[i] = next;
                next += (uint)Align(_hunk.Segments[i].DeclaredSizeBytes + 0x100, 0x100);
            }
        }

        private void LoadSegments()
        {
            for (var i = 0; i < _hunk.Segments.Count; i++)
            {
                Bus.CopyToChipRam(_segmentBases[i], _hunk.Segments[i].Data);
            }
        }

        private void ApplyRelocations()
        {
            foreach (var segment in _hunk.Segments)
            {
                var segmentBase = _segmentBases[segment.Index];
                foreach (var block in segment.Relocations)
                {
                    var targetBase = _segmentBases[block.TargetSegmentIndex];
                    foreach (var offset in block.Offsets)
                    {
                        if (offset < 0 || offset + 4 > segment.Data.Length)
                        {
                            throw new ModuleLoadException("A CUST relocation offset is outside its source segment.");
                        }

                        var address = segmentBase + (uint)offset;
                        var oldValue = Bus.ReadLong(address);
                        Bus.WriteLong(address, oldValue + targetBase);
                    }
                }
            }
        }

        private IReadOnlyDictionary<uint, uint> BuildAbsoluteTags()
        {
            var result = new Dictionary<uint, uint>();
            var baseAddress = _segmentBases[_rawTags.SegmentIndex];
            var cursor = baseAddress + (uint)_rawTags.Offset;
            for (var i = 0; i < 64; i++)
            {
                var tag = Bus.ReadLong(cursor);
                cursor += 4;
                if (tag == CustConstants.TagDone)
                {
                    break;
                }

                var value = Bus.ReadLong(cursor);
                cursor += 4;
                result[tag] = value;
            }

            return result;
        }

        private void InstallHostEnvironment()
        {
            Bus.RegisterHostCallback(_hostGetListDataAddress, HostGetListData);
            Bus.RegisterHostCallback(_hostOkAddress, HostOk);
            Bus.RegisterHostCallback(_hostSongEndAddress, HostSongEnd);
            WriteHostWord(CustConstants.DtgSoundNumberOffset, (ushort)(_firstSubSongNumber + _currentSubSongIndex));
            WriteHostLong(CustConstants.DtgAudioAllocOffset, _hostOkAddress);
            WriteHostLong(CustConstants.DtgGetListDataOffset, _hostGetListDataAddress);
            WriteHostLong(CustConstants.DtgAudioFreeOffset, _hostOkAddress);
            WriteHostLong(CustConstants.DtgSongEndOffset, _hostSongEndAddress);
            WriteHostWord(CustConstants.DtgTimerOffset, 0);
        }

        private void HostGetListData(M68kCpuState state)
        {
            var segment = _hunk.Segments[_listDataSegmentIndex];
            state.A[0] = _segmentBases[_listDataSegmentIndex];
            state.D[0] = (uint)segment.DeclaredSizeBytes;
        }

        private void HostOk(M68kCpuState state)
        {
            state.D[0] = 0;
        }

        private void HostSongEnd(M68kCpuState state)
        {
            _ = state;
            _songEnded = true;
        }

        private void UpdateSubSongRange()
        {
            if (!_tags.TryGetValue(CustConstants.DtpSubSongRange, out var address) || address == 0)
            {
                _subSongCount = 1;
                _firstSubSongNumber = 0;
                return;
            }

            RunSubroutine(address, CustConstants.SubroutineCycleBudget);
            var min = (int)(Cpu.State.D[0] & 0xFFFF);
            var max = (int)(Cpu.State.D[1] & 0xFFFF);
            if (max >= min && max > 0)
            {
                _firstSubSongNumber = min;
                _subSongCount = Math.Clamp(max - min + 1, 1, 256);
            }
            else
            {
                _subSongCount = 1;
                _firstSubSongNumber = 0;
            }
        }

        private void CallIfPresent(uint tag, long? budget = null, bool advancePaulaAtEnd = true)
        {
            if (!_tags.TryGetValue(tag, out var address) || address == 0)
            {
                return;
            }

            RunSubroutine(address, budget ?? CustConstants.SubroutineCycleBudget, advancePaulaAtEnd);
        }

        private void RunSubroutine(uint address, long maxCycles, bool advancePaulaAtEnd = true)
        {
            var startCycles = Cpu.State.Cycles;
            var instructions = 0;
            var stopwatch = Stopwatch.StartNew();
            Cpu.BeginSubroutine(address, CustConstants.StackTopAddress, 0xFFFF_FFFC);
            Cpu.State.A[5] = CustConstants.HostBlockAddress;
            try
            {
                while (!Cpu.State.Halted &&
                    Cpu.State.ProgramCounter != 0xFFFF_FFFC &&
                    Cpu.State.Cycles - startCycles < maxCycles &&
                    instructions < CustConstants.SubroutineInstructionBudget &&
                    stopwatch.ElapsedMilliseconds < CustConstants.SubroutineWallClockBudgetMilliseconds)
                {
                    Cpu.ExecuteInstruction();
                    instructions++;
                }

                if (Cpu.State.ProgramCounter != 0xFFFF_FFFC &&
                    (Cpu.State.Cycles - startCycles >= maxCycles ||
                    instructions >= CustConstants.SubroutineInstructionBudget ||
                    stopwatch.ElapsedMilliseconds >= CustConstants.SubroutineWallClockBudgetMilliseconds))
                {
                    AddDiagnostic(
                        ModuleDiagnosticSeverity.Warning,
                        $"CUST replay code exceeded its cycle budget at PC 0x{Cpu.State.LastInstructionProgramCounter:X8}, opcode 0x{Cpu.State.LastOpcode:X4}.",
                        "CUST_CPU_OVERRUN");
                    Cpu.State.Halted = true;
                }
            }
            catch (UnsupportedM68kOpcodeException ex)
            {
                AddDiagnostic(ModuleDiagnosticSeverity.Warning, ex.Message, "CUST_UNSUPPORTED_OPCODE");
                Cpu.State.Halted = true;
            }
            catch (ModuleLoadException ex)
            {
                AddDiagnostic(
                    ModuleDiagnosticSeverity.Warning,
                    ex.Message + $" Last opcode 0x{Cpu.State.LastOpcode:X4} at PC 0x{Cpu.State.LastInstructionProgramCounter:X8}, current PC 0x{Cpu.State.ProgramCounter:X8}.",
                    "CUST_CPU_FAULT");
                Cpu.State.Halted = true;
            }

            if (advancePaulaAtEnd)
            {
                Bus.Paula.AdvanceTo(Cpu.State.Cycles);
            }
            var timer = Bus.ReadWord(CustConstants.HostBlockAddress + CustConstants.DtgTimerOffset);
            if (timer != 0)
            {
                QuantumCycleCount = Math.Max(1, (long)Math.Round((timer + 1) * 10.0));
            }
        }

        private void AddDiagnostic(ModuleDiagnosticSeverity severity, string message, string code)
        {
            if (_diagnostics.Count >= 32)
            {
                return;
            }

            _diagnostics.Add(new ModuleDiagnostic(severity, message, code));
        }

        private void WriteHostWord(int offset, ushort value)
        {
            Bus.WriteWord(CustConstants.HostBlockAddress + (uint)offset, value);
        }

        private void WriteHostLong(int offset, uint value)
        {
            Bus.WriteLong(CustConstants.HostBlockAddress + (uint)offset, value);
        }

        private static int Align(int value, int alignment)
        {
            return ((value + alignment - 1) / alignment) * alignment;
        }

        private static int ResolveListDataSegmentIndex(HunkFile hunk, DeliTagTable tags)
        {
            if (hunk.Segments.Count == 1)
            {
                return 0;
            }

            var bestIndex = -1;
            var bestSize = -1;
            foreach (var segment in hunk.Segments)
            {
                if (segment.Index == tags.SegmentIndex || segment.Kind == HunkSegmentKind.Bss)
                {
                    continue;
                }

                if (segment.DeclaredSizeBytes > bestSize)
                {
                    bestIndex = segment.Index;
                    bestSize = segment.DeclaredSizeBytes;
                }
            }

            return bestIndex >= 0 ? bestIndex : 0;
        }
    }
}
