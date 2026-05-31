using System;
using System.Collections.Generic;
using System.Diagnostics;
using CopperMod.Abstractions;
using CopperMod.Amiga;

namespace CopperMod.Cust
{
    internal sealed class CustMachine
    {
        private const long HostInterruptIntervalCycles = 1_420;
        private const long HostInterruptCycleBudget = 12_000;
        private const long MaxHostInterruptCycleBudget = 120_000;
        private const int MaxCiaInterruptsPerDispatch = 512;
        private const int MaxPaulaInterruptsPerDispatch = 512;
        private const uint DmaconAddress = 0x00DFF096;
        private const ushort DmaconSetMasterDma = 0x8200;
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
        private readonly uint _hostAudioAllocAddress = CustConstants.HostCallbackBaseAddress + 0x50;
        private readonly uint _hostAudioFreeAddress = CustConstants.HostCallbackBaseAddress + 0x60;
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
        private int _renderedQuanta;

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
            : this(hunk, tags, loadContext, cpuFactory, cpuBackend, AmigaKickstartConfiguration.HostShim13)
        {
        }

        public CustMachine(
            HunkFile hunk,
            DeliTagTable tags,
            ModuleLoadContext? loadContext,
            IM68kCoreFactory cpuFactory,
            M68kBackendKind cpuBackend,
            AmigaKickstartConfiguration kickstartConfiguration)
        {
            _hunk = hunk ?? throw new ArgumentNullException(nameof(hunk));
            _rawTags = tags ?? throw new ArgumentNullException(nameof(tags));
            _loadContext = loadContext;
            _segmentBases = new uint[hunk.Segments.Count];
            _listDataSegmentIndex = ResolveListDataSegmentIndex(hunk, tags);
            Machine = new AmigaMachine(
                AmigaMachineOptions
                    .ForProfile(AmigaMachineProfile.A500PalCustPlayback)
                    .WithCpu(cpuFactory ?? throw new ArgumentNullException(nameof(cpuFactory)), cpuBackend)
                    .WithKickstart(kickstartConfiguration));
            Bus = Machine.Bus;
            Cpu = Machine.Cpu;
            Reset(0);
        }

        public AmigaMachine Machine { get; }

        public AmigaBus Bus { get; }

        public IM68kCore Cpu { get; }

        public IReadOnlyList<ModuleDiagnostic> Diagnostics => _diagnostics;

        public IReadOnlyList<CustomRegisterWrite> CustomRegisterWrites => Bus.CustomRegisterWrites;

        public int SubSongCount => _subSongCount;

        public int CurrentSubSongIndex => _currentSubSongIndex;

        public bool SongEnded => _songEnded;

        public bool AudioFilterEnabled => Bus.AudioFilterEnabled;

        public string KickstartDescription => Machine.Kickstart.Configuration.Description;

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
            _renderedQuanta = 0;
            Machine.ResetHardware();
            EnableHostDmaMaster();
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
            CallIfPresent(
                CustConstants.DtpVolume,
                prepare: state =>
                {
                    state.D[0] = 64;
                    state.D[1] = 64;
                });

            AdvanceTimedHardwareTo(Cpu.State.Cycles);
        }

        private void EnableHostDmaMaster()
        {
            Bus.WriteDeviceWord(
                AmigaBusRequester.Host,
                AmigaBusAccessKind.CustomRegister,
                DmaconAddress,
                DmaconSetMasterDma,
                0);
            Bus.Paula.AdvanceTo(0);
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
                Bus.AdvanceRasterTo(targetCycle);
                Bus.Paula.RenderSample(targetCycle, destination, frame, channels);
                DispatchPendingPaulaInterrupts(targetCycle);
                var sampleOffset = frame * channels;
                for (var channel = 0; channel < channels; channel++)
                {
                    peak = Math.Max(peak, Math.Abs(destination[sampleOffset + channel]));
                }
            }

            AdvanceTimedHardwareTo(endCycle);
            DispatchPendingPaulaInterrupts(endCycle);
            if (Cpu.State.Cycles < endCycle)
            {
                Cpu.State.Cycles = endCycle;
            }

            if (peak <= 0.0001f && ShouldRenderFallbackPcm())
            {
                RenderFallbackPcm(destination, frames, channels, sampleRate);
            }

            LastChannelWaveform = captureChannels ? ConvertWaveform(Bus.Paula.FinishChannelCapture()) : null;
            _renderedQuanta++;
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
            Bus.RegisterHostCallback(_hostGetListDataAddress, HostGetListData);
            Bus.RegisterHostCallback(_hostOkAddress, HostOk);
            Bus.RegisterHostCallback(_hostSongEndAddress, HostSongEnd);
            Bus.RegisterHostCallback(_hostResetPathAddress, HostResetPath);
            Bus.RegisterHostCallback(_hostAppendPathOrOkAddress, HostAppendPathOrOk);
            Bus.RegisterHostCallback(_hostAudioAllocAddress, HostAudioAlloc);
            Bus.RegisterHostCallback(_hostAudioFreeAddress, HostAudioFree);
            Machine.Kickstart.Install(
                Bus,
                new AmigaKickstartTrapTable(
                    _hostOkAddress,
                    HostNullCallback,
                    HostOk,
                    HostOpenLibrary,
                    HostAllocMem,
                    HostAllocAndStore,
                    HostFreeMem,
                    HostCauseInterrupt,
                    HostAddInterrupt,
                    HostRemoveInterrupt,
                    HostAbleIcr,
                    HostSetIcr,
                    HostDosOpen,
                    HostDosClose,
                    HostDosRead,
                    HostDosSeek));

            WriteNullTerminatedString(AmigaKickstartHost.HostPathBufferAddress, string.Empty, AmigaKickstartHost.HostPathBufferLength);

            WriteHostLong(CustConstants.DtgDosBaseOffset, AmigaKickstartHost.DosLibraryBase);
            WriteHostLong(CustConstants.DtgExecBaseOffset, AmigaKickstartHost.ExecLibraryBase);
            WriteHostLong(CustConstants.DtgPathBufferOffset, AmigaKickstartHost.HostPathBufferAddress);
            WriteHostWord(CustConstants.DtgSoundNumberOffset, (ushort)(_firstSubSongNumber + _currentSubSongIndex));
            WriteHostWord(CustConstants.DtgSoundVolumeOffset, 64);
            WriteHostWord(CustConstants.DtgSoundLeftBalanceOffset, 64);
            WriteHostWord(CustConstants.DtgSoundRightBalanceOffset, 64);
            WriteHostLong(CustConstants.DtgResetPathOffset, _hostResetPathAddress);
            WriteHostLong(CustConstants.DtgAudioAllocOffset, _hostAudioAllocAddress);
            WriteHostLong(CustConstants.DtgGetListDataOffset, _hostGetListDataAddress);
            WriteHostLong(CustConstants.DtgAudioFreeOffset, _hostAudioFreeAddress);
            WriteHostLong(CustConstants.DtgSongEndOffset, _hostSongEndAddress);
            WriteHostWord(CustConstants.DtgTimerOffset, 0);
        }

        private void HostGetListData(M68kCpuState state)
        {
            var address = _segmentBases[_listDataSegmentIndex];
            state.A[0] = address;
            state.D[0] = address;
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
                state.D[0] = AmigaKickstartHost.DosLibraryBase;
            }
            else if (name.IndexOf("ciaa", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                state.D[0] = AmigaKickstartHost.CiaAResourceBase;
            }
            else if (name.IndexOf("ciab", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                state.D[0] = AmigaKickstartHost.CiaBResourceBase;
            }
            else if (name.IndexOf("cia", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                state.D[0] = AmigaKickstartHost.CiaBResourceBase;
            }
            else if (name.IndexOf("req", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                state.D[0] = AmigaKickstartHost.ReqLibraryBase;
            }
            else
            {
                state.D[0] = AmigaKickstartHost.DummyLibraryBase;
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
            if (TryGetCiaResource(state.A[6], out var cia))
            {
                var mask = DecodeCiaIcrVectorMask(state.D[0]);
                CaptureInterruptServer(state.A[1], cia, mask);
                Bus.AbleCiaInterrupts(cia, (byte)(0x80 | mask), state.Cycles);
            }
            else
            {
                CaptureInterruptServer(state.A[1], paulaMask: DecodePaulaInterruptMask(state.D[0]));
            }

            state.D[0] = 0;
        }

        private void HostRemoveInterrupt(M68kCpuState state)
        {
            if (TryGetCiaResource(state.A[6], out var cia))
            {
                RemoveInterruptServer(state.A[1], cia, DecodeCiaIcrVectorMask(state.D[0]));
            }
            else
            {
                RemoveInterruptServer(state.A[1], paulaMask: DecodePaulaInterruptMask(state.D[0]));
            }

            state.D[0] = 0;
        }

        private void HostAbleIcr(M68kCpuState state)
        {
            if (!TryGetCiaResource(state.A[6], out var cia))
            {
                state.D[0] = 0;
                return;
            }

            state.D[0] = Bus.AbleCiaInterrupts(cia, (byte)state.D[0], state.Cycles);
        }

        private void HostSetIcr(M68kCpuState state)
        {
            if (!TryGetCiaResource(state.A[6], out var cia))
            {
                state.D[0] = 0;
                return;
            }

            state.D[0] = Bus.SetCiaInterrupts(cia, (byte)state.D[0], state.Cycles);
        }

        private void HostCauseInterrupt(M68kCpuState state)
        {
            CaptureInterruptServer(state.A[1], paulaMask: DecodePaulaInterruptMask(state.D[0]));
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

        private void CaptureInterruptServer(uint interruptAddress, AmigaCiaId? cia = null, byte icrMask = 0, ushort paulaMask = 0)
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
                if (existing.Code == code &&
                    existing.Data == data &&
                    existing.Cia == cia &&
                    existing.IcrMask == icrMask &&
                    existing.PaulaMask == paulaMask)
                {
                    return;
                }
            }

            _installedInterrupts.Add(new InterruptServer(code, data, cia, icrMask, paulaMask));
        }

        private void RemoveInterruptServer(uint interruptAddress, AmigaCiaId? cia = null, byte icrMask = 0, ushort paulaMask = 0)
        {
            if (interruptAddress == 0)
            {
                _installedInterrupts.RemoveAll(server =>
                    (!cia.HasValue || server.Cia == cia) &&
                    (paulaMask == 0 || (server.PaulaMask & paulaMask) != 0));
                _installedInterruptAddress = 0;
                _installedInterruptData = 0;
                return;
            }

            var code = Bus.ReadLong(interruptAddress + 18);
            _installedInterrupts.RemoveAll(server =>
                server.Code == code &&
                (!cia.HasValue || server.Cia == cia) &&
                (icrMask == 0 || (server.IcrMask & icrMask) != 0) &&
                (paulaMask == 0 || (server.PaulaMask & paulaMask) != 0));
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
            WriteNullTerminatedString(AmigaKickstartHost.HostPathBufferAddress, _hostPath, AmigaKickstartHost.HostPathBufferLength);
        }

        private void HostAppendPathOrOk(M68kCpuState state)
        {
            var value = ReadNullTerminatedString(state.A[0], 128);
            if (LooksLikePathFragment(value))
            {
                _hostPath += value.Replace('\\', '/');
                WriteNullTerminatedString(AmigaKickstartHost.HostPathBufferAddress, _hostPath, AmigaKickstartHost.HostPathBufferLength);
            }

            state.D[0] = 0;
        }

        private void HostAudioAlloc(M68kCpuState state)
        {
            state.D[0] = 1;
        }

        private void HostAudioFree(M68kCpuState state)
        {
            state.D[0] = 0;
        }

        private void HostDosOpen(M68kCpuState state)
        {
            var path = NormalizeAmigaPath(ReadNullTerminatedString(state.D[1], AmigaKickstartHost.HostPathBufferLength));
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
            var savedCycle = Cpu.State.Cycles;
            DispatchCiaInterruptsUpTo(endCycle);
            Cpu.State.Cycles = Math.Max(savedCycle, Cpu.State.Cycles);
        }

        private void DispatchCiaInterruptsUpTo(long targetCycle)
        {
            if (_tags.ContainsKey(CustConstants.DtpInterrupt))
            {
                Bus.AdvanceCiasTo(targetCycle);
                Bus.DrainCiaInterrupts();
                return;
            }

            var dispatched = 0;
            while (dispatched < MaxCiaInterruptsPerDispatch)
            {
                DispatchPendingCiaInterrupts(targetCycle, ref dispatched);
                var nextCycle = Bus.GetNextCiaInterruptCycle(targetCycle);
                if (!nextCycle.HasValue)
                {
                    break;
                }

                Bus.AdvanceCiasTo(nextCycle.Value);
            }

            Bus.AdvanceCiasTo(targetCycle);
            DispatchPendingCiaInterrupts(targetCycle, ref dispatched);
            if (dispatched >= MaxCiaInterruptsPerDispatch)
            {
                AddDiagnostic(
                    ModuleDiagnosticSeverity.Warning,
                    "CUST CIA interrupt dispatch exceeded its per-quantum guard.",
                    "CUST_CPU_OVERRUN");
            }
        }

        private void DispatchPendingCiaInterrupts(long targetCycle, ref int dispatched)
        {
            var events = Bus.DrainCiaInterrupts();
            foreach (var interruptEvent in events)
            {
                if (interruptEvent.Cycle > targetCycle)
                {
                    continue;
                }

                Cpu.State.Cycles = Math.Max(Cpu.State.Cycles, interruptEvent.Cycle);
                CallInstalledInterrupt(interruptEvent);
                dispatched++;
                if (dispatched >= MaxCiaInterruptsPerDispatch)
                {
                    return;
                }
            }
        }

        private void CallInstalledInterrupt(AmigaCiaInterruptEvent interruptEvent)
        {
            var servers = new List<InterruptServer>();
            foreach (var server in _installedInterrupts)
            {
                if (server.Cia == interruptEvent.Cia &&
                    server.IcrMask != 0 &&
                    (interruptEvent.IcrBits & server.IcrMask) != 0)
                {
                    servers.Add(server);
                }
            }

            if (servers.Count == 0)
            {
                return;
            }

            var budget = GetHostInterruptCycleBudget(GetRecoveryIntervalCycles()) * servers.Count;
            var perServerBudget = Math.Max(1, budget / servers.Count);
            foreach (var server in servers)
            {
                RunIsolatedSubroutine(
                    server.Code,
                    perServerBudget,
                    prepare: state => PrepareInterruptServerState(state, server.Data));
            }
        }

        private void DispatchPendingPaulaInterrupts(long targetCycle)
        {
            if (_insideHostInterrupt)
            {
                Bus.Paula.DrainInterrupts();
                return;
            }

            var dispatched = 0;
            foreach (var interruptEvent in Bus.Paula.DrainInterrupts())
            {
                if (interruptEvent.Cycle > targetCycle)
                {
                    continue;
                }

                Cpu.State.Cycles = Math.Max(Cpu.State.Cycles, interruptEvent.Cycle);
                CallInstalledInterrupt(interruptEvent);
                dispatched++;
                if (dispatched >= MaxPaulaInterruptsPerDispatch)
                {
                    AddDiagnostic(
                        ModuleDiagnosticSeverity.Warning,
                        "CUST Paula interrupt dispatch exceeded its per-quantum guard.",
                        "CUST_CPU_OVERRUN");
                    return;
                }
            }
        }

        private void CallInstalledInterrupt(PaulaInterruptEvent interruptEvent)
        {
            var servers = new List<InterruptServer>();
            foreach (var server in _installedInterrupts)
            {
                if (server.PaulaMask != 0 && (server.PaulaMask & interruptEvent.IntreqBit) != 0)
                {
                    servers.Add(server);
                }
            }

            if (servers.Count == 0)
            {
                return;
            }

            var perServerBudget = Math.Max(1, HostInterruptCycleBudget / servers.Count);
            foreach (var server in servers)
            {
                RunIsolatedSubroutine(
                    server.Code,
                    perServerBudget,
                    prepare: state => PrepareInterruptServerState(state, server.Data));
            }
        }

        private static void PrepareInterruptServerState(M68kCpuState state, uint data)
        {
            state.A[0] = data;
            state.A[1] = data;
            state.A[6] = AmigaKickstartHost.ExecLibraryBase;
        }

        private static bool TryGetCiaResource(uint resourceBase, out AmigaCiaId cia)
        {
            if (resourceBase == AmigaKickstartHost.CiaAResourceBase)
            {
                cia = AmigaCiaId.A;
                return true;
            }

            if (resourceBase == AmigaKickstartHost.CiaBResourceBase)
            {
                cia = AmigaCiaId.B;
                return true;
            }

            cia = default;
            return false;
        }

        private static byte DecodeCiaIcrVectorMask(uint value)
        {
            var lowBits = (byte)(value & 0x1F);
            if (lowBits == 0)
            {
                return AmigaCia.TimerAInterruptMask;
            }

            if (lowBits < 5)
            {
                return (byte)(lowBits | (1 << lowBits));
            }

            return lowBits;
        }

        private static ushort DecodePaulaInterruptMask(uint value)
        {
            var explicitMask = (ushort)(value & 0x0780);
            if (explicitMask != 0)
            {
                return explicitMask;
            }

            var bitNumber = (int)(value & 0x1F);
            return bitNumber is >= 7 and <= 10 ? (ushort)(1 << bitNumber) : (ushort)0;
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
                    AdvanceTimedHardwareTo(Cpu.State.Cycles);
                    if (!_insideHostInterrupt)
                    {
                        DispatchCiaInterruptsUpTo(Cpu.State.Cycles);
                        DispatchPendingPaulaInterrupts(Cpu.State.Cycles);
                    }

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
            catch (AmigaEmulationException ex)
            {
                AddDiagnostic(
                    ModuleDiagnosticSeverity.Warning,
                    ex.Message + $" Last opcode 0x{Cpu.State.LastOpcode:X4} at PC 0x{Cpu.State.LastInstructionProgramCounter:X8}, current PC 0x{Cpu.State.ProgramCounter:X8}.",
                    "CUST_CPU_FAULT");
                Cpu.State.Halted = true;
            }

            if (advancePaulaAtEnd)
            {
                AdvanceTimedHardwareTo(Cpu.State.Cycles);
            }
            var timer = Bus.ReadWord(CustConstants.HostBlockAddress + CustConstants.DtgTimerOffset);
            if (timer != 0)
            {
                QuantumCycleCount = Math.Max(1, (long)Math.Round((timer + 1) * 10.0));
            }
        }

        private void AdvanceTimedHardwareTo(long targetCycle)
        {
            Bus.AdvanceRasterTo(targetCycle);
            Bus.Paula.AdvanceTo(targetCycle);
        }

        private bool TryRecoverHostInterruptWait()
        {
            if (_insideHostInterrupt || _tags.ContainsKey(CustConstants.DtpInterrupt) || _installedInterrupts.Count == 0)
            {
                return false;
            }

            for (var attempt = 0; attempt < 64; attempt++)
            {
                DispatchCiaInterruptsUpTo(Cpu.State.Cycles + GetRecoveryIntervalCycles());
                var startCycles = Cpu.State.Cycles;
                var instructions = 0;
                while (!Cpu.State.Halted &&
                    Cpu.State.ProgramCounter != 0xFFFF_FFFC &&
                    Cpu.State.Cycles - startCycles < GetRecoveryIntervalCycles() * 4 &&
                    instructions < 4_000)
                {
                    Cpu.ExecuteInstruction();
                    AdvanceTimedHardwareTo(Cpu.State.Cycles);
                    DispatchCiaInterruptsUpTo(Cpu.State.Cycles);
                    DispatchPendingPaulaInterrupts(Cpu.State.Cycles);
                    instructions++;
                }

                if (Cpu.State.ProgramCounter == 0xFFFF_FFFC)
                {
                    return true;
                }
            }

            return false;
        }

        private long GetRecoveryIntervalCycles()
        {
            var nextCycle = Bus.GetNextCiaInterruptCycle(Cpu.State.Cycles + MaxHostInterruptCycleBudget);
            if (nextCycle.HasValue && nextCycle.Value > Cpu.State.Cycles)
            {
                return nextCycle.Value - Cpu.State.Cycles;
            }

            return Bus.CiaBTimerAIntervalCycles > 0 ? Bus.CiaBTimerAIntervalCycles : HostInterruptIntervalCycles;
        }

        private static long GetHostInterruptCycleBudget(long interval)
        {
            return Math.Clamp(interval, HostInterruptCycleBudget, MaxHostInterruptCycleBudget);
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

        private static ModuleChannelWaveform? ConvertWaveform(AmigaChannelWaveform? waveform)
        {
            if (waveform == null)
            {
                return null;
            }

            var channels = new ModuleChannelWaveformChannel[waveform.Channels.Count];
            for (var i = 0; i < channels.Length; i++)
            {
                var channel = waveform.Channels[i];
                channels[i] = new ModuleChannelWaveformChannel(channel.Index, channel.Samples, channel.IsActive);
            }

            return new ModuleChannelWaveform(channels, waveform.FrameCount, waveform.SampleRate);
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

        private bool ShouldRenderFallbackPcm()
        {
            if (_tags.ContainsKey(CustConstants.DtpInterrupt))
            {
                return false;
            }

            if (_installedInterrupts.Count == 0)
            {
                return true;
            }

            if (HasInstalledCiaInterruptServer() &&
                Bus.GetNextCiaInterruptCycle(Cpu.State.Cycles + Math.Max(MaxHostInterruptCycleBudget, QuantumCycleCount * 8)) != null)
            {
                return false;
            }

            return _renderedQuanta >= 2 && !HasAudioChannelRegisterWrites();
        }

        private bool HasInstalledCiaInterruptServer()
        {
            foreach (var server in _installedInterrupts)
            {
                if (server.Cia.HasValue)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasAudioChannelRegisterWrites()
        {
            foreach (var write in CustomRegisterWrites)
            {
                if (write.Address >= 0x0A0 && write.Address <= 0x0DA)
                {
                    return true;
                }
            }

            return false;
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
            public InterruptServer(uint code, uint data, AmigaCiaId? cia, byte icrMask, ushort paulaMask)
            {
                Code = code;
                Data = data;
                Cia = cia;
                IcrMask = icrMask;
                PaulaMask = paulaMask;
            }

            public uint Code { get; }

            public uint Data { get; }

            public AmigaCiaId? Cia { get; }

            public byte IcrMask { get; }

            public ushort PaulaMask { get; }
        }
    }
}
