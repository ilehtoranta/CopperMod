using System;
using System.Collections.Generic;
using System.Diagnostics;
using CopperMod.Abstractions;

namespace CopperMod.Cust
{
    internal sealed class CustMachine
    {
        private const uint ExecLibraryBase = 0x00F1_0000;
        private const uint DosLibraryBase = 0x00F2_0000;
        private const uint CiaBResourceBase = 0x00F3_0000;
        private const uint ReqLibraryBase = 0x00F4_0000;
        private const uint DummyLibraryBase = 0x00F5_0000;
        private const uint ExecStructAddress = 0x0000_2000;
        private const uint HostPathBufferAddress = 0x0000_3000;
        private const int HostPathBufferLength = 512;
        private const long HostInterruptIntervalCycles = 1_420;
        private const long HostInterruptCycleBudget = 12_000;
        private readonly HunkFile _hunk;
        private readonly DeliTagTable _rawTags;
        private readonly ModuleLoadContext? _loadContext;
        private readonly List<ModuleDiagnostic> _diagnostics = new List<ModuleDiagnostic>();
        private readonly uint[] _segmentBases;
        private readonly Dictionary<uint, ExternalFileHandle> _fileHandles = new Dictionary<uint, ExternalFileHandle>();
        private readonly Dictionary<uint, byte[]> _allocations = new Dictionary<uint, byte[]>();
        private readonly int _listDataSegmentIndex;
        private readonly uint _hostGetListDataAddress = CustConstants.HostCallbackBaseAddress;
        private readonly uint _hostOkAddress = CustConstants.HostCallbackBaseAddress + 0x10;
        private readonly uint _hostSongEndAddress = CustConstants.HostCallbackBaseAddress + 0x20;
        private readonly uint _hostResetPathAddress = CustConstants.HostCallbackBaseAddress + 0x30;
        private readonly uint _hostAppendPathOrOkAddress = CustConstants.HostCallbackBaseAddress + 0x40;
        private IReadOnlyDictionary<uint, uint> _tags = new Dictionary<uint, uint>();
        private uint _nextExternalAllocationAddress = 0x0010_0000;
        private uint _nextFileHandle = 0x100;
        private string _hostPath = string.Empty;
        private uint _installedInterruptAddress;
        private uint _installedInterruptData;
        private readonly List<InterruptServer> _installedInterrupts = new List<InterruptServer>();
        private bool _insideHostInterrupt;
        private int _subSongCount = 1;
        private int _firstSubSongNumber;
        private int _currentSubSongIndex;
        private bool _songEnded;
        private bool _fallbackPcmDiagnosticAdded;
        private uint _fallbackPcmAddress;
        private int _fallbackPcmLength;
        private double _fallbackPcmPosition;

        public CustMachine(HunkFile hunk, DeliTagTable tags)
            : this(hunk, tags, null, M68kCoreFactory.Default, M68kBackendKind.AccurateM68000)
        {
        }

        public CustMachine(HunkFile hunk, DeliTagTable tags, ModuleLoadContext? loadContext)
            : this(hunk, tags, loadContext, M68kCoreFactory.Default, M68kBackendKind.AccurateM68000)
        {
        }

        public CustMachine(HunkFile hunk, DeliTagTable tags, IM68kCoreFactory cpuFactory, M68kBackendKind cpuBackend)
            : this(hunk, tags, null, cpuFactory, cpuBackend)
        {
        }

        public CustMachine(HunkFile hunk, DeliTagTable tags, ModuleLoadContext? loadContext, IM68kCoreFactory cpuFactory, M68kBackendKind cpuBackend)
        {
            _hunk = hunk ?? throw new ArgumentNullException(nameof(hunk));
            _rawTags = tags ?? throw new ArgumentNullException(nameof(tags));
            _loadContext = loadContext;
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
            _fileHandles.Clear();
            _allocations.Clear();
            _nextFileHandle = 0x100;
            _hostPath = string.Empty;
            _installedInterruptAddress = 0;
            _installedInterruptData = 0;
            _installedInterrupts.Clear();
            _fallbackPcmDiagnosticAdded = false;
            _fallbackPcmAddress = 0;
            _fallbackPcmLength = 0;
            _fallbackPcmPosition = 0;
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

            if (_tags.ContainsKey(CustConstants.DtpInterrupt))
            {
                CallIfPresent(CustConstants.DtpInterrupt, QuantumCycleCount, advancePaulaAtEnd: false);
            }
            else
            {
                DispatchInstalledInterruptsForQuantum(startCycle, endCycle);
            }

            var cyclesPerOutputFrame = QuantumCycleCount / (double)frames;
            var peak = 0.0f;
            for (var frame = 0; frame < frames; frame++)
            {
                var targetCycle = startCycle + (long)Math.Round((frame + 1) * cyclesPerOutputFrame);
                Bus.Paula.RenderSample(targetCycle, destination, frame, channels);
                var sampleOffset = frame * channels;
                for (var channel = 0; channel < channels; channel++)
                {
                    peak = Math.Max(peak, Math.Abs(destination[sampleOffset + channel]));
                }
            }

            Bus.Paula.AdvanceTo(endCycle);
            if (Cpu.State.Cycles < endCycle)
            {
                Cpu.State.Cycles = endCycle;
            }

            if (peak <= 0.0001f && !_tags.ContainsKey(CustConstants.DtpInterrupt))
            {
                RenderFallbackPcm(destination, frames, channels, sampleRate);
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

            _nextExternalAllocationAddress = Math.Max(0x0010_0000u, (uint)Align(checked((int)next), 0x100));
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
            Bus.RegisterHostCallback(0, HostNullCallback);
            Bus.RegisterHostCallback(_hostGetListDataAddress, HostGetListData);
            Bus.RegisterHostCallback(_hostOkAddress, HostOk);
            Bus.RegisterHostCallback(_hostSongEndAddress, HostSongEnd);
            Bus.RegisterHostCallback(_hostResetPathAddress, HostResetPath);
            Bus.RegisterHostCallback(_hostAppendPathOrOkAddress, HostAppendPathOrOk);
            RegisterLibraryCallbacks();

            Bus.WriteLong(0, ExecStructAddress);
            Bus.WriteLong(4, ExecLibraryBase);
            Bus.WriteLong(ExecStructAddress + 0x68, _hostOkAddress);
            WriteNullTerminatedString(HostPathBufferAddress, string.Empty, HostPathBufferLength);

            WriteHostLong(CustConstants.DtgDosBaseOffset, DosLibraryBase);
            WriteHostLong(CustConstants.DtgExecBaseOffset, ExecLibraryBase);
            WriteHostLong(CustConstants.DtgPathBufferOffset, HostPathBufferAddress);
            WriteHostWord(CustConstants.DtgSoundNumberOffset, (ushort)(_firstSubSongNumber + _currentSubSongIndex));
            WriteHostWord(CustConstants.DtgSoundVolumeOffset, 64);
            WriteHostWord(CustConstants.DtgSoundLeftBalanceOffset, 64);
            WriteHostWord(CustConstants.DtgSoundRightBalanceOffset, 64);
            WriteHostLong(CustConstants.DtgResetPathOffset, _hostResetPathAddress);
            WriteHostLong(CustConstants.DtgAudioAllocOffset, _hostAppendPathOrOkAddress);
            WriteHostLong(CustConstants.DtgGetListDataOffset, _hostGetListDataAddress);
            WriteHostLong(CustConstants.DtgAudioFreeOffset, _hostOkAddress);
            WriteHostLong(CustConstants.DtgSongEndOffset, _hostSongEndAddress);
            WriteHostWord(CustConstants.DtgTimerOffset, 0);
        }

        private void RegisterLibraryCallbacks()
        {
            RegisterLibraryCallback(ExecLibraryBase, -408, HostOpenLibrary);
            RegisterLibraryCallback(ExecLibraryBase, -498, HostOpenLibrary);
            RegisterLibraryCallback(ExecLibraryBase, -414, HostOk);
            RegisterLibraryCallback(ExecLibraryBase, -396, HostOk);
            RegisterLibraryCallback(ExecLibraryBase, -198, HostAllocMem);
            RegisterLibraryCallback(ExecLibraryBase, -210, HostFreeMem);
            RegisterLibraryCallback(ExecLibraryBase, -180, HostCauseInterrupt);
            RegisterLibraryCallback(ExecLibraryBase, -174, HostRemoveInterrupt);
            RegisterLibraryCallback(ExecLibraryBase, -168, HostAddInterrupt);
            RegisterLibraryCallback(ExecLibraryBase, -162, HostAddInterrupt);
            RegisterLibraryCallback(ExecLibraryBase, -396, HostAllocAndStore);

            RegisterLibraryCallback(DosLibraryBase, -30, HostDosOpen);
            RegisterLibraryCallback(DosLibraryBase, -36, HostDosClose);
            RegisterLibraryCallback(DosLibraryBase, -42, HostDosRead);
            RegisterLibraryCallback(DosLibraryBase, -66, HostDosSeek);
            RegisterLibraryCallback(DosLibraryBase, -174, HostOk);
            RegisterLibraryCallback(DosLibraryBase, -180, HostOk);
            RegisterLibraryCallback(DosLibraryBase, -396, HostOk);
            RegisterLibraryCallback(DosLibraryBase, -408, HostOpenLibrary);

            RegisterLibraryCallback(CiaBResourceBase, -6, HostAddInterrupt);
            RegisterLibraryCallback(CiaBResourceBase, -12, HostRemoveInterrupt);
            RegisterLibraryCallback(CiaBResourceBase, -18, HostOk);
            RegisterLibraryCallback(CiaBResourceBase, -24, HostOk);

            RegisterLibraryCallback(ReqLibraryBase, -6, HostOk);
            RegisterLibraryCallback(ReqLibraryBase, -12, HostOk);
            RegisterLibraryCallback(ReqLibraryBase, -174, HostOk);
            RegisterLibraryCallback(ReqLibraryBase, -180, HostOk);
            RegisterLibraryCallback(ReqLibraryBase, -396, HostOk);
            RegisterLibraryCallback(ReqLibraryBase, -408, HostOpenLibrary);

            RegisterLibraryCallback(DummyLibraryBase, -6, HostOk);
            RegisterLibraryCallback(DummyLibraryBase, -12, HostOk);
            RegisterLibraryCallback(DummyLibraryBase, -174, HostOk);
            RegisterLibraryCallback(DummyLibraryBase, -180, HostOk);
            RegisterLibraryCallback(DummyLibraryBase, -396, HostOk);
            RegisterLibraryCallback(DummyLibraryBase, -408, HostOpenLibrary);
        }

        private void RegisterLibraryCallback(uint libraryBase, int displacement, Action<M68kCpuState> callback)
        {
            Bus.RegisterHostCallback(unchecked((uint)((int)libraryBase + displacement)), callback);
        }

        private void HostGetListData(M68kCpuState state)
        {
            state.A[0] = _segmentBases[_listDataSegmentIndex];
            state.D[0] = 0;
        }

        private void HostOk(M68kCpuState state)
        {
            state.D[0] = 0;
        }

        private void HostNullCallback(M68kCpuState state)
        {
            _ = state;
            AddDiagnostic(
                ModuleDiagnosticSeverity.Warning,
                "CUST replay code called a null host callback; treating it as a no-op.",
                "CUST_NULL_CALLBACK");
        }

        private void HostSongEnd(M68kCpuState state)
        {
            _ = state;
            _songEnded = true;
        }

        private void HostOpenLibrary(M68kCpuState state)
        {
            var name = ReadNullTerminatedString(state.A[1], 96);
            if (name.IndexOf("dos", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                state.D[0] = DosLibraryBase;
            }
            else if (name.IndexOf("cia", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                state.D[0] = CiaBResourceBase;
            }
            else if (name.IndexOf("req", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                state.D[0] = ReqLibraryBase;
            }
            else
            {
                state.D[0] = DummyLibraryBase;
            }
        }

        private void HostAllocMem(M68kCpuState state)
        {
            var size = Math.Max(1, (int)(state.D[0] & 0x00FF_FFFF));
            var address = AllocateExternalMemory(size);
            _allocations[address] = new byte[size];
            state.D[0] = address;
        }

        private void HostAllocAndStore(M68kCpuState state)
        {
            HostAllocMem(state);
            if (state.A[0] != 0 && state.A[0] + 4 <= Bus.ChipRam.Length)
            {
                Bus.WriteLong(state.A[0], state.D[0], state.Cycles);
            }
        }

        private void HostFreeMem(M68kCpuState state)
        {
            _allocations.Remove(state.A[1]);
            state.D[0] = 0;
        }

        private void HostAddInterrupt(M68kCpuState state)
        {
            CaptureInterruptServer(state.A[1]);
            state.D[0] = 0;
        }

        private void HostRemoveInterrupt(M68kCpuState state)
        {
            RemoveInterruptServer(state.A[1]);
        }

        private void HostCauseInterrupt(M68kCpuState state)
        {
            CaptureInterruptServer(state.A[1]);
            if (_insideHostInterrupt)
            {
                state.D[0] = 0;
                return;
            }

            if (_installedInterruptAddress != 0)
            {
                RunIsolatedSubroutine(
                    _installedInterruptAddress,
                    HostInterruptCycleBudget,
                    prepare: cpuState => PrepareInterruptServerState(cpuState, _installedInterruptData));
            }

            state.D[0] = 0;
        }

        private void CaptureInterruptServer(uint interruptAddress)
        {
            if (interruptAddress == 0)
            {
                return;
            }

            var data = Bus.ReadLong(interruptAddress + 14);
            var code = Bus.ReadLong(interruptAddress + 18);
            if (code == 0)
            {
                return;
            }

            _installedInterruptData = data;
            _installedInterruptAddress = code;
            foreach (var existing in _installedInterrupts)
            {
                if (existing.Code == code && existing.Data == data)
                {
                    return;
                }
            }

            _installedInterrupts.Add(new InterruptServer(code, data));
        }

        private void RemoveInterruptServer(uint interruptAddress)
        {
            if (interruptAddress == 0)
            {
                _installedInterrupts.Clear();
                _installedInterruptAddress = 0;
                _installedInterruptData = 0;
                return;
            }

            var code = Bus.ReadLong(interruptAddress + 18);
            _installedInterrupts.RemoveAll(server => server.Code == code);
            if (_installedInterruptAddress == code)
            {
                _installedInterruptAddress = 0;
                _installedInterruptData = 0;
            }
        }

        private void HostResetPath(M68kCpuState state)
        {
            _ = state;
            _hostPath = string.Empty;
            WriteNullTerminatedString(HostPathBufferAddress, _hostPath, HostPathBufferLength);
        }

        private void HostAppendPathOrOk(M68kCpuState state)
        {
            var value = ReadNullTerminatedString(state.A[0], 128);
            if (LooksLikePathFragment(value))
            {
                _hostPath += value.Replace('\\', '/');
                WriteNullTerminatedString(HostPathBufferAddress, _hostPath, HostPathBufferLength);
            }

            state.D[0] = 0;
        }

        private void HostDosOpen(M68kCpuState state)
        {
            var path = NormalizeAmigaPath(ReadNullTerminatedString(state.D[1], HostPathBufferLength));
            if (string.IsNullOrWhiteSpace(path))
            {
                path = NormalizeAmigaPath(_hostPath);
            }

            if (_loadContext == null || !_loadContext.TryReadRelativeFile(path, out var data))
            {
                AddDiagnostic(
                    ModuleDiagnosticSeverity.Warning,
                    $"CUST external data file is unavailable: {path}",
                    "CUST_EXTERNAL_DATA_MISSING");
                state.D[0] = 0;
                return;
            }

            var handle = _nextFileHandle++;
            _fileHandles[handle] = new ExternalFileHandle(path, data);
            state.D[0] = handle;
        }

        private void HostDosClose(M68kCpuState state)
        {
            _fileHandles.Remove(state.D[1]);
            state.D[0] = 0;
        }

        private void HostDosRead(M68kCpuState state)
        {
            if (!_fileHandles.TryGetValue(state.D[1], out var handle))
            {
                state.D[0] = 0xFFFF_FFFF;
                return;
            }

            var requested = (int)Math.Min(state.D[3], int.MaxValue);
            var available = Math.Max(0, handle.Data.Length - handle.Position);
            var count = Math.Min(requested, available);
            if (count > 0)
            {
                handle.Data.AsSpan(handle.Position, count).CopyTo(Bus.ChipRam.AsSpan((int)state.D[2], count));
                handle.Position += count;
            }

            state.D[0] = (uint)count;
        }

        private void HostDosSeek(M68kCpuState state)
        {
            if (!_fileHandles.TryGetValue(state.D[1], out var handle))
            {
                state.D[0] = 0xFFFF_FFFF;
                return;
            }

            var oldPosition = handle.Position;
            var offset = unchecked((int)state.D[2]);
            var mode = unchecked((int)state.D[3]);
            var origin = mode switch
            {
                1 => handle.Data.Length,
                -1 => 0,
                _ => handle.Position
            };
            handle.Position = Math.Clamp(origin + offset, 0, handle.Data.Length);
            state.D[0] = (uint)oldPosition;
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

        private void CallIfPresent(
            uint tag,
            long? budget = null,
            bool advancePaulaAtEnd = true,
            Action<M68kCpuState>? prepare = null)
        {
            if (!_tags.TryGetValue(tag, out var address) || address == 0)
            {
                return;
            }

            RunSubroutine(address, budget ?? CustConstants.SubroutineCycleBudget, advancePaulaAtEnd, prepare);
        }

        private void CallInstalledInterrupt(long budget)
        {
            if (_installedInterrupts.Count == 0)
            {
                return;
            }

            var perServerBudget = Math.Max(1, budget / _installedInterrupts.Count);
            foreach (var server in _installedInterrupts.ToArray())
            {
                RunIsolatedSubroutine(
                    server.Code,
                    perServerBudget,
                    prepare: state => PrepareInterruptServerState(state, server.Data));
            }
        }

        private void DispatchInstalledInterruptsForQuantum(long startCycle, long endCycle)
        {
            if (_installedInterrupts.Count == 0)
            {
                return;
            }

            var savedCycle = Cpu.State.Cycles;
            for (var cycle = startCycle + HostInterruptIntervalCycles; cycle <= endCycle; cycle += HostInterruptIntervalCycles)
            {
                Cpu.State.Cycles = Math.Max(Cpu.State.Cycles, cycle);
                CallInstalledInterrupt(HostInterruptCycleBudget * _installedInterrupts.Count);
            }

            Cpu.State.Cycles = Math.Max(savedCycle, Cpu.State.Cycles);
        }

        private static void PrepareInterruptServerState(M68kCpuState state, uint data)
        {
            state.A[0] = data;
            state.A[1] = data;
            state.A[6] = ExecLibraryBase;
        }

        private void RunSubroutine(
            uint address,
            long maxCycles,
            bool advancePaulaAtEnd = true,
            Action<M68kCpuState>? prepare = null)
        {
            var startCycles = Cpu.State.Cycles;
            var instructions = 0;
            var stopwatch = Stopwatch.StartNew();
            Cpu.BeginSubroutine(address, CustConstants.StackTopAddress, 0xFFFF_FFFC);
            Cpu.State.A[5] = CustConstants.HostBlockAddress;
            prepare?.Invoke(Cpu.State);
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
                    if (!TryRecoverHostInterruptWait())
                    {
                        AddDiagnostic(
                            ModuleDiagnosticSeverity.Warning,
                            $"CUST replay code exceeded its cycle budget at PC 0x{Cpu.State.LastInstructionProgramCounter:X8}, opcode 0x{Cpu.State.LastOpcode:X4}.",
                            "CUST_CPU_OVERRUN");
                        Cpu.State.Halted = true;
                    }
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

        private bool TryRecoverHostInterruptWait()
        {
            if (_insideHostInterrupt || _tags.ContainsKey(CustConstants.DtpInterrupt) || _installedInterrupts.Count == 0)
            {
                return false;
            }

            for (var attempt = 0; attempt < 64; attempt++)
            {
                CallInstalledInterrupt(HostInterruptCycleBudget * _installedInterrupts.Count);
                var startCycles = Cpu.State.Cycles;
                var instructions = 0;
                while (!Cpu.State.Halted &&
                    Cpu.State.ProgramCounter != 0xFFFF_FFFC &&
                    Cpu.State.Cycles - startCycles < HostInterruptIntervalCycles * 4 &&
                    instructions < 4_000)
                {
                    Cpu.ExecuteInstruction();
                    instructions++;
                }

                if (Cpu.State.ProgramCounter == 0xFFFF_FFFC)
                {
                    return true;
                }
            }

            return false;
        }

        private void RunIsolatedSubroutine(
            uint address,
            long maxCycles,
            Action<M68kCpuState>? prepare = null)
        {
            if (address == 0)
            {
                return;
            }

            var savedD = new uint[Cpu.State.D.Length];
            var savedA = new uint[Cpu.State.A.Length];
            Array.Copy(Cpu.State.D, savedD, savedD.Length);
            Array.Copy(Cpu.State.A, savedA, savedA.Length);
            var savedPc = Cpu.State.ProgramCounter;
            var savedSr = Cpu.State.StatusRegister;
            var savedHalted = Cpu.State.Halted;
            var savedLastOpcode = Cpu.State.LastOpcode;
            var savedLastPc = Cpu.State.LastInstructionProgramCounter;
            var savedCycles = Cpu.State.Cycles;
            var wasInsideHostInterrupt = _insideHostInterrupt;
            var elapsedCycles = 0L;
            try
            {
                _insideHostInterrupt = true;
                RunSubroutine(address, maxCycles, advancePaulaAtEnd: false, prepare);
                elapsedCycles = Math.Max(0, Cpu.State.Cycles - savedCycles);
            }
            finally
            {
                _insideHostInterrupt = wasInsideHostInterrupt;
                Array.Copy(savedD, Cpu.State.D, savedD.Length);
                Array.Copy(savedA, Cpu.State.A, savedA.Length);
                Cpu.State.ProgramCounter = savedPc;
                Cpu.State.StatusRegister = savedSr;
                Cpu.State.Halted = savedHalted;
                Cpu.State.LastOpcode = savedLastOpcode;
                Cpu.State.LastInstructionProgramCounter = savedLastPc;
                Cpu.State.Cycles = savedCycles + elapsedCycles;
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

        private void RenderFallbackPcm(Span<float> destination, int frames, int channels, int sampleRate)
        {
            if (!EnsureFallbackPcmSource())
            {
                return;
            }

            if (!_fallbackPcmDiagnosticAdded)
            {
                AddDiagnostic(
                    ModuleDiagnosticSeverity.Info,
                    "CUST replay did not produce audible Paula output; streaming loaded sample memory as a compatibility fallback.",
                    "CUST_FALLBACK_PCM");
                _fallbackPcmDiagnosticAdded = true;
            }

            var step = 8_000.0 / Math.Max(1, sampleRate);
            for (var frame = 0; frame < frames; frame++)
            {
                var sourceOffset = (int)_fallbackPcmPosition % _fallbackPcmLength;
                var value = unchecked((sbyte)Bus.ChipRam[(int)_fallbackPcmAddress + sourceOffset]) / 128.0f * 0.20f;
                var offset = frame * channels;
                if (channels == 1)
                {
                    destination[offset] = value;
                }
                else
                {
                    destination[offset] = value;
                    destination[offset + 1] = value;
                    for (var extra = 2; extra < channels; extra++)
                    {
                        destination[offset + extra] = value;
                    }
                }

                _fallbackPcmPosition += step;
                if (_fallbackPcmPosition >= _fallbackPcmLength)
                {
                    _fallbackPcmPosition -= _fallbackPcmLength;
                }
            }
        }

        private bool EnsureFallbackPcmSource()
        {
            if (_fallbackPcmLength > 0)
            {
                return true;
            }

            var memory = Bus.ChipRam;
            var bestAddress = 0;
            var bestLength = 0;
            long bestScore = 0;
            const int windowLength = 4096;
            for (var address = (int)CustConstants.DefaultModuleBaseAddress; address + windowLength <= memory.Length; address += 512)
            {
                long score = 0;
                var nonZero = 0;
                for (var i = 0; i < windowLength; i++)
                {
                    var value = unchecked((sbyte)memory[address + i]);
                    var magnitude = Math.Abs((int)value);
                    score += magnitude;
                    if (magnitude > 2)
                    {
                        nonZero++;
                    }
                }

                if (nonZero < windowLength / 8 || score <= bestScore)
                {
                    continue;
                }

                bestScore = score;
                bestAddress = address;
                bestLength = windowLength;
            }

            if (bestLength == 0)
            {
                return false;
            }

            _fallbackPcmAddress = (uint)bestAddress;
            _fallbackPcmLength = bestLength;
            _fallbackPcmPosition = 0;
            return true;
        }

        private uint AllocateExternalMemory(int size)
        {
            var address = _nextExternalAllocationAddress;
            _nextExternalAllocationAddress += (uint)Align(size + 1, 2);
            if (_nextExternalAllocationAddress >= CustConstants.StackTopAddress)
            {
                AddDiagnostic(
                    ModuleDiagnosticSeverity.Warning,
                    "CUST external allocation exceeded the emulated chip RAM budget.",
                    "CUST_EXTERNAL_DATA_MISSING");
                return 0;
            }

            return address;
        }

        private string ReadNullTerminatedString(uint address, int maxLength)
        {
            if (address == 0 || maxLength <= 0)
            {
                return string.Empty;
            }

            Span<char> chars = stackalloc char[Math.Min(maxLength, 256)];
            var count = 0;
            while (count < chars.Length)
            {
                var value = Bus.ReadByte(address + (uint)count);
                if (value == 0)
                {
                    break;
                }

                chars[count++] = (char)value;
            }

            return new string(chars.Slice(0, count));
        }

        private void WriteNullTerminatedString(uint address, string value, int maxLength)
        {
            var length = Math.Min(Math.Max(0, maxLength - 1), value.Length);
            for (var i = 0; i < length; i++)
            {
                Bus.WriteByte(address + (uint)i, (byte)value[i], Cpu.State.Cycles);
            }

            Bus.WriteByte(address + (uint)length, 0, Cpu.State.Cycles);
        }

        private static bool LooksLikePathFragment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            foreach (var character in value)
            {
                if (character < 32 || character > 126)
                {
                    return false;
                }
            }

            return true;
        }

        private static string NormalizeAmigaPath(string path)
        {
            path = path.Trim().Replace('\\', '/');
            var colon = path.IndexOf(':');
            if (colon >= 0)
            {
                path = path.Substring(colon + 1);
            }

            while (path.StartsWith("/", StringComparison.Ordinal))
            {
                path = path.Substring(1);
            }

            return path;
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

        private sealed class ExternalFileHandle
        {
            public ExternalFileHandle(string path, byte[] data)
            {
                Path = path;
                Data = data;
            }

            public string Path { get; }

            public byte[] Data { get; }

            public int Position { get; set; }
        }

        private readonly struct InterruptServer
        {
            public InterruptServer(uint code, uint data)
            {
                Code = code;
                Data = data;
            }

            public uint Code { get; }

            public uint Data { get; }
        }
    }
}
