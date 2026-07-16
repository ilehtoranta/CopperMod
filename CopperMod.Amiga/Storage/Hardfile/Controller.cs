/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections.Generic;

namespace CopperMod.Amiga.Storage.Hardfile
{
    internal sealed class CopperHdfController : AutoconfigBoard, IDisposable
    {
        public const string DeviceName = "copperhdf.device";
        public const uint AutoConfigBase = AutoconfigChain.ZorroIIConfigBase;
        public const uint AutoConfigSize = AutoconfigChain.ConfigSize;
        public const uint BoardSize = 0x0001_0000;
        public const ushort ManufacturerId = 0x07DB;
        public const byte ProductId = 0x48;
        public const int DiagAreaOffset = 0x4000;
        public const int DiagAreaCopySize = 0x1000;
        public const int DiagPointOffset = 0x0020;
        public const int BootPointOffset = 0x0030;
        public const int ResidentOffset = 0x0040;
        public const int BootstrapDataOffset = 0x00C0;
        public const int NameOffset = 0x0100;
        public const int IdStringOffset = 0x0120;
        public const int ResidentInitOffset = 0x0140;
        public const int DeviceBaseOffset = 0x0200;
        public const int PerUnitDataOffset = 0x0400;
        public const int UnitTableOffset = PerUnitDataOffset;
        public const int DosNodeOffset = PerUnitDataOffset + 0x30;
        public const int FileSysStartupMsgOffset = PerUnitDataOffset + 0x60;
        public const int DosEnvecOffset = PerUnitDataOffset + 0x70;
        public const int BootNodeOffset = PerUnitDataOffset + 0xC0;
        public const int DosDeviceNameOffset = PerUnitDataOffset + 0xE0;
        public const int ExecDeviceNameBstrOffset = PerUnitDataOffset + 0xE8;

        private const byte IoErrOpenFail = 0xFF;
        private const byte IoErrBadLength = 0xFE;
        private const byte IoErrBadAddress = 0xFD;
        private const byte IoErrWriteProtected = 0xFC;
        private const byte IoErrUnsupported = 0xFB;

        private const ushort CmdRead = 2;
        private const ushort CmdWrite = 3;
        private const ushort CmdUpdate = 4;
        private const ushort CmdClear = 5;
        private const ushort CmdFlush = 8;
        private const ushort TdChangeNum = 13;
        private const ushort TdChangeState = 14;
        private const ushort TdProtStatus = 15;
        private const ushort TdGetNumTracks = 19;
        private const ushort TdRead64 = 24;
        private const ushort TdWrite64 = 25;
        private const ushort TdSeek64 = 26;
        private const ushort TdFormat64 = 27;
        private const ushort HdScsiCmd = 28;
        private const ushort NscmdDeviceQuery = 0x4000;

        private const int IoUnitOffset = 0x18;
        private const int IoCommandOffset = 0x1C;
        private const int IoErrorOffset = 0x1F;
        private const int IoActualOffset = 0x20;
        private const int IoLengthOffset = 0x24;
        private const int IoDataOffset = 0x28;
        private const int IoOffsetOffset = 0x2C;
        private const int IoFlagsOffset = 0x1E;
        private const int IoDeviceOffset = 0x14;
        private const int IoUnitOffsetInRequest = 0x18;

        private const uint MemfPublic = 0x0000_0001;
        private const uint MemfClear = 0x0001_0000;
        private const uint DosTypeOFS = AmigaDosEnvec.DosTypeOfs;
        private const uint NullBlockPointer = 0xFFFF_FFFF;
        private const int ExecResourceListOffset = 0x0150;
        private const int ExecDeviceListOffset = 0x015E;
        private const int ExecMemListOffset = 0x0142;
        private const int ExpansionMountListOffset = 0x004A;
        private const int ConfigDevFlagsOffset = 0x0E;
        private const int ConfigDevDriverOffset = 0x28;
        private const byte ConfigDevConfigMeFlag = 0x02;
        private const byte NodeTypeDevice = 3;
        private const byte NodeTypeResource = 8;
        private const byte NodeTypeBootNode = 16;
        private const int LibrarySize = 0x22;
        private const int DeviceTrapVectorSize = 6 * 6;
        private const int PerUnitDataSize = 0x0100;
        private const int UnitMessageListOffset = 0x14;
        private const int UnitOpenCountOffset = 0x24;
        private const int MemHeaderAttributesOffset = 0x0E;
        private const int MemHeaderFirstChunkOffset = 0x10;
        private const int MemHeaderFreeOffset = 0x1C;
        private const int MemChunkNextOffset = 0x00;
        private const int MemChunkBytesOffset = 0x04;
        private const int FileSystemResourceSize = 0x20;
        private const int FileSystemResourceEntriesOffset = 0x12;
        private const int FileSystemEntrySize = 0x3E;
        private const int NodeNameOffset = 0x0A;
        private const int DeviceNodeTypeOffset = 0x04;
        private const int DeviceNodeTaskOffset = 0x08;
        private const int DeviceNodeLockOffset = 0x0C;
        private const int DeviceNodeHandlerOffset = 0x10;
        private const int DeviceNodeStackSizeOffset = 0x14;
        private const int DeviceNodePriorityOffset = 0x18;
        private const int DeviceNodeStartupOffset = 0x1C;
        private const int DeviceNodeSegListOffset = 0x20;
        private const int DeviceNodeGlobalVecOffset = 0x24;
        private const int DeviceNodeNameOffset = 0x28;
        private const uint FileSystemPatchType = 1u << 0;
        private const uint FileSystemPatchTask = 1u << 1;
        private const uint FileSystemPatchLock = 1u << 2;
        private const uint FileSystemPatchHandler = 1u << 3;
        private const uint FileSystemPatchStackSize = 1u << 4;
        private const uint FileSystemPatchPriority = 1u << 5;
        private const uint FileSystemPatchStartup = 1u << 6;
        private const uint FileSystemPatchSegList = 1u << 7;
        private const uint FileSystemPatchGlobalVec = 1u << 8;

        private readonly Dictionary<int, AmigaHardfile> _hardfiles = new Dictionary<int, AmigaHardfile>();
        private readonly byte[] _boardRom;
        private AmigaBus? _bootstrapBus;
        private bool _bootstrapInstalled;
        private uint _diagCopyBase;
        private uint _execBase;
        private uint _expansionBase;
        private uint _configDev;
        private uint _deviceBase;
        private readonly Dictionary<int, uint> _unitAddresses = new Dictionary<int, uint>();

        public CopperHdfController(IEnumerable<AmigaHardfileConfiguration> configurations)
            : base(AutoconfigIdentity.CreateIoBoard(
                checked((int)BoardSize),
                ManufacturerId,
                ProductId,
                checked((ushort)DiagAreaOffset)))
        {
            ArgumentNullException.ThrowIfNull(configurations);
            foreach (var configuration in configurations)
            {
                var hardfile = AmigaHardfile.Open(configuration);
                if (_hardfiles.ContainsKey(hardfile.Unit))
                {
                    hardfile.Dispose();
                    throw new AmigaEmulationException($"Duplicate CopperHDF unit {configuration.Unit}.");
                }

                _hardfiles.Add(hardfile.Unit, hardfile);
            }

            _boardRom = CreateBoardRom();
        }

        public override bool IsPresent => _hardfiles.Count != 0;

        public bool BootstrapInstalled => _bootstrapInstalled;

        public bool DiagBootstrapCalled { get; private set; }

        public bool BootBootstrapCalled { get; private set; }

        public bool ResidentInitCalled { get; private set; }

        public bool DeviceRegistered { get; private set; }

        public bool BootNodeRegistered { get; private set; }

        public uint DeviceBase => _deviceBase;

        public uint BootNodeAddress { get; private set; }

        public uint DeviceNodeAddress { get; private set; }

        public IReadOnlyDictionary<int, AmigaHardfile> Hardfiles => _hardfiles;

        public override bool ContainsBoardAddress(uint address)
        {
            return IsPresent &&
                IsConfigured &&
                address >= ConfiguredBase &&
                address - ConfiguredBase < BoardSize;
        }

        public override byte ReadBoardByte(uint address)
        {
            var offset = (int)((address - ConfiguredBase) & (BoardSize - 1));
            return _boardRom[offset];
        }

        public override bool TryWriteBoardByte(uint address, byte value)
        {
            _ = address;
            _ = value;
            return ContainsBoardAddress(address);
        }

        public void InstallBootstrapTraps(AmigaBus bus)
        {
            ArgumentNullException.ThrowIfNull(bus);
            _bootstrapBus = bus;
            WriteTrap(DiagPointOffset, bus.RegisterRelocatableHostTrapStub(HostDiagBootstrap));
            WriteTrap(BootPointOffset, bus.RegisterRelocatableHostTrapStub(HostBootBootstrap));
            WriteTrap(ResidentInitOffset, bus.RegisterRelocatableHostTrapStub(HostResidentInit));
            _bootstrapInstalled = true;
        }

        public bool TryExecuteIoRequest(AmigaBus bus, uint ioRequestAddress)
        {
            ArgumentNullException.ThrowIfNull(bus);
            if (ioRequestAddress == 0 || !bus.IsMappedMemoryRange(ioRequestAddress, 0x30))
            {
                return false;
            }

            var unit = ReadUnit(bus, ioRequestAddress);
            if (!_hardfiles.TryGetValue(unit, out var hardfile))
            {
                CompleteIo(bus, ioRequestAddress, IoErrOpenFail, 0);
                return true;
            }

            var command = bus.ReadWord(ioRequestAddress + IoCommandOffset);
            try
            {
                switch (command)
                {
                    case CmdRead:
                        ExecuteRead(bus, ioRequestAddress, hardfile);
                        return true;
                    case CmdWrite:
                        ExecuteWrite(bus, ioRequestAddress, hardfile);
                        return true;
                    case TdRead64:
                        ExecuteRead64(bus, ioRequestAddress, hardfile);
                        return true;
                    case TdWrite64:
                    case TdFormat64:
                        ExecuteWrite64(bus, ioRequestAddress, hardfile);
                        return true;
                    case TdSeek64:
                        ExecuteSeek64(bus, ioRequestAddress, hardfile);
                        return true;
                    case CmdUpdate:
                    case CmdFlush:
                        hardfile.Flush();
                        CompleteIo(bus, ioRequestAddress, 0, 0);
                        return true;
                    case CmdClear:
                        CompleteIo(bus, ioRequestAddress, 0, 0);
                        return true;
                    case TdChangeNum:
                    case TdChangeState:
                        CompleteIo(bus, ioRequestAddress, 0, 0);
                        bus.WriteLong(ioRequestAddress + IoActualOffset, 0);
                        return true;
                    case TdProtStatus:
                        CompleteIo(bus, ioRequestAddress, 0, hardfile.ReadOnly ? 1u : 0u);
                        return true;
                    case TdGetNumTracks:
                        CompleteIo(bus, ioRequestAddress, 0, (uint)Math.Min(uint.MaxValue, hardfile.SectorCount));
                        return true;
                    case HdScsiCmd:
                        ExecuteScsiCommand(bus, ioRequestAddress, hardfile);
                        return true;
                    case NscmdDeviceQuery:
                        ExecuteDeviceQuery(bus, ioRequestAddress);
                        return true;
                    default:
                        CompleteIo(bus, ioRequestAddress, IoErrUnsupported, 0);
                        return true;
                }
            }
            catch (UnauthorizedAccessException)
            {
                CompleteIo(bus, ioRequestAddress, IoErrWriteProtected, 0);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                CompleteIo(bus, ioRequestAddress, IoErrBadLength, 0);
                return true;
            }
            catch (OverflowException)
            {
                CompleteIo(bus, ioRequestAddress, IoErrBadLength, 0);
                return true;
            }
            catch (AmigaEmulationException)
            {
                CompleteIo(bus, ioRequestAddress, IoErrBadAddress, 0);
                return true;
            }
        }

        public override void ResetConfiguration()
        {
            base.ResetConfiguration();
            _bootstrapBus = null;
            _bootstrapInstalled = false;
            _diagCopyBase = 0;
            _execBase = 0;
            _expansionBase = 0;
            _configDev = 0;
            _deviceBase = 0;
            _unitAddresses.Clear();
            DiagBootstrapCalled = false;
            BootBootstrapCalled = false;
            ResidentInitCalled = false;
            DeviceRegistered = false;
            BootNodeRegistered = false;
            BootNodeAddress = 0;
            DeviceNodeAddress = 0;
        }

        public void Dispose()
        {
            foreach (var hardfile in _hardfiles.Values)
            {
                hardfile.Dispose();
            }

            _hardfiles.Clear();
        }

        private void HostDiagBootstrap(M68kCpuState state)
        {
            DiagBootstrapCalled = true;
            var copyBase = state.A[2] & 0x00FF_FFFFu;
            _diagCopyBase = copyBase;
            _execBase = (state.A[6] != 0 ? state.A[6] : _bootstrapBus?.ReadLong(4) ?? 0) & 0x00FF_FFFFu;
            _expansionBase = state.A[5] & 0x00FF_FFFFu;
            _configDev = state.A[3] & 0x00FF_FFFFu;
            if (_bootstrapBus != null &&
                copyBase != 0 &&
                _bootstrapBus.IsMappedMemoryRange(copyBase, DiagAreaCopySize))
            {
                PatchResidentPointers(_bootstrapBus, copyBase);
                WriteBootstrapData(_bootstrapBus, copyBase, _execBase, _expansionBase, _configDev, state.A[0] & 0x00FF_FFFFu);
            }

            state.D[0] = 1;
        }

        private void HostBootBootstrap(M68kCpuState state)
        {
            BootBootstrapCalled = true;
            RegisterSystemIntegration(state);
            state.D[0] = 1;
        }

        private void HostResidentInit(M68kCpuState state)
        {
            ResidentInitCalled = true;
            RegisterSystemIntegration(state);
            state.D[0] = _deviceBase != 0 ? _deviceBase : 1;
        }

        private void RegisterSystemIntegration(M68kCpuState state)
        {
            var bus = _bootstrapBus;
            if (bus == null)
            {
                return;
            }

            var copyBase = _diagCopyBase != 0 ? _diagCopyBase : FindCopyBaseFromResidentInit(state);
            var execBase = _execBase != 0 ? _execBase : bus.ReadLong(4);
            var expansionBase = _expansionBase;
            var configDev = _configDev;
            if (copyBase == 0 || execBase == 0 || !bus.IsMappedMemoryRange(copyBase, DiagAreaCopySize))
            {
                return;
            }

            RegisterExecDevice(bus, copyBase, execBase, configDev);
            RegisterBootNodes(bus, copyBase, execBase, expansionBase);
        }

        private static void PatchResidentPointers(AmigaBus bus, uint copyBase)
        {
            var resident = copyBase + ResidentOffset;
            bus.WriteLong(resident + 0x02, resident);
            bus.WriteLong(resident + 0x06, copyBase + ResidentInitOffset + 4u);
            bus.WriteLong(resident + 0x0E, copyBase + NameOffset);
            bus.WriteLong(resident + 0x12, copyBase + IdStringOffset);
            bus.WriteLong(resident + 0x16, copyBase + ResidentInitOffset);
        }

        private uint FindCopyBaseFromResidentInit(M68kCpuState state)
        {
            var pc = state.LastInstructionProgramCounter & 0x00FF_FFFFu;
            return pc >= ResidentInitOffset ? pc - ResidentInitOffset : 0;
        }

        private static void WriteBootstrapData(AmigaBus bus, uint copyBase, uint execBase, uint expansionBase, uint configDev, uint boardBase)
        {
            var data = copyBase + BootstrapDataOffset;
            bus.WriteLong(data + 0x00, execBase);
            bus.WriteLong(data + 0x04, expansionBase);
            bus.WriteLong(data + 0x08, configDev);
            bus.WriteLong(data + 0x0C, boardBase);
        }

        private void RegisterExecDevice(AmigaBus bus, uint copyBase, uint execBase, uint configDev)
        {
            if (DeviceRegistered)
            {
                return;
            }

            _deviceBase = copyBase + DeviceBaseOffset;
            bus.ClearMemory(_deviceBase - DeviceTrapVectorSize, DeviceTrapVectorSize + LibrarySize);
            bus.WriteByte(_deviceBase + 0x08, NodeTypeDevice, 0);
            bus.WriteByte(_deviceBase + 0x09, 20, 0);
            bus.WriteLong(_deviceBase + 0x0A, copyBase + NameOffset);
            bus.WriteWord(_deviceBase + 0x10, DeviceTrapVectorSize);
            bus.WriteWord(_deviceBase + 0x12, LibrarySize);
            bus.WriteWord(_deviceBase + 0x14, 1);
            bus.WriteWord(_deviceBase + 0x16, 0);
            bus.WriteLong(_deviceBase + 0x18, copyBase + IdStringOffset);

            RegisterDeviceTrap(bus, -6, HostDeviceOpen);
            RegisterDeviceTrap(bus, -12, HostDeviceClose);
            RegisterDeviceTrap(bus, -18, HostDeviceExpunge);
            RegisterDeviceTrap(bus, -24, HostDeviceExtFunc);
            RegisterDeviceTrap(bus, -30, HostDeviceBeginIo);
            RegisterDeviceTrap(bus, -36, HostDeviceAbortIo);
            LinkTail(bus, execBase + ExecDeviceListOffset, _deviceBase);

            if (configDev != 0 && bus.IsMappedMemoryRange(configDev, 0x30))
            {
                var flags = bus.ReadByte(configDev + ConfigDevFlagsOffset);
                bus.WriteByte(configDev + ConfigDevFlagsOffset, (byte)(flags & ~ConfigDevConfigMeFlag), 0);
                bus.WriteLong(configDev + ConfigDevDriverOffset, _deviceBase);
            }

            DeviceRegistered = true;
        }

        private void RegisterBootNodes(AmigaBus bus, uint copyBase, uint execBase, uint expansionBase)
        {
            if (BootNodeRegistered)
            {
                return;
            }

            var allocator = new BootMetadataAllocator(bus, copyBase, execBase);
            foreach (var hardfile in _hardfiles.Values)
            {
                var unitAddress = allocator.Allocate(0x30);
                if (unitAddress == 0)
                {
                    continue;
                }

                _unitAddresses[hardfile.Unit] = unitAddress;
                WriteUnit(bus, unitAddress);
            }

            var partitions = MaterializePartitions();
            var fileSystems = LoadMatchingFileSystems(bus, allocator, partitions);
            for (var partitionIndex = 0; partitionIndex < partitions.Count; partitionIndex++)
            {
                var partition = partitions[partitionIndex];
                if (!_unitAddresses.TryGetValue(partition.Unit, out _))
                {
                    continue;
                }

                var dosNode = allocator.Allocate(0x2C);
                var startup = allocator.Allocate(0x10);
                var envec = allocator.Allocate(AmigaDosEnvec.LongCount * 4);
                var bootNode = allocator.Allocate(0x20);
                var dosName = allocator.Allocate(partition.DeviceName.Length + 1);
                var dosNameBstr = allocator.Allocate(partition.DeviceName.Length + 2);
                var deviceNameBstr = allocator.Allocate(DeviceName.Length + 2);
                if (dosNode == 0 || startup == 0 || envec == 0 || bootNode == 0 || dosName == 0 || dosNameBstr == 0 || deviceNameBstr == 0)
                {
                    continue;
                }

                WriteCString(bus, dosName, partition.DeviceName);
                WriteBstr(bus, dosNameBstr, partition.DeviceName);
                WriteBstr(bus, deviceNameBstr, DeviceName);
                WriteDosEnvec(bus, envec, partition.Environment);
                WriteFileSysStartupMsg(bus, startup, partition.Unit, deviceNameBstr, envec);
                fileSystems.TryGetValue(partition.Environment.DosType, out var fileSystem);
                WriteDeviceNode(bus, dosNode, startup, dosNameBstr, fileSystem);
                WriteBootNode(bus, bootNode, dosNode, dosName, partition.BootPriority);
                if (partitionIndex == 0)
                {
                    DeviceNodeAddress = dosNode;
                    BootNodeAddress = bootNode;
                }

                if (expansionBase != 0 && bus.IsMappedMemoryRange(expansionBase + ExpansionMountListOffset, 14))
                {
                    LinkTail(bus, expansionBase + ExpansionMountListOffset, bootNode);
                    BootNodeRegistered = true;
                }
            }
        }

        private List<AmigaHardfilePartition> MaterializePartitions()
        {
            var partitions = new List<AmigaHardfilePartition>();
            foreach (var hardfile in _hardfiles.Values)
            {
                partitions.AddRange(hardfile.GetMountablePartitions());
            }

            return partitions;
        }

        private Dictionary<uint, LoadedFileSystem> LoadMatchingFileSystems(
            AmigaBus bus,
            BootMetadataAllocator allocator,
            IReadOnlyList<AmigaHardfilePartition> partitions)
        {
            var usedDosTypes = new HashSet<uint>();
            foreach (var partition in partitions)
            {
                usedDosTypes.Add(partition.Environment.DosType);
            }

            if (usedDosTypes.Count == 0)
            {
                return new Dictionary<uint, LoadedFileSystem>();
            }

            var bestFileSystems = new Dictionary<uint, AmigaRdbFileSystem>();
            foreach (var hardfile in _hardfiles.Values)
            {
                foreach (var fileSystem in hardfile.GetRigidDiskBlockFileSystems())
                {
                    if (!usedDosTypes.Contains(fileSystem.DosType))
                    {
                        continue;
                    }

                    if (!bestFileSystems.TryGetValue(fileSystem.DosType, out var current) ||
                        fileSystem.Version > current.Version)
                    {
                        bestFileSystems[fileSystem.DosType] = fileSystem;
                    }
                }
            }

            if (bestFileSystems.Count == 0)
            {
                return new Dictionary<uint, LoadedFileSystem>();
            }

            var loaded = new Dictionary<uint, LoadedFileSystem>();
            var resource = EnsureFileSystemResource(bus, allocator);
            foreach (var entry in bestFileSystems.Values)
            {
                var segmentList = 0u;
                if (entry.LoadSegData.Length != 0 &&
                    !TryLoadHunkSegmentList(bus, allocator, entry.LoadSegData, out segmentList) &&
                    (entry.PatchFlags & FileSystemPatchSegList) != 0)
                {
                    continue;
                }

                var loadedEntry = new LoadedFileSystem(entry, segmentList);
                loaded[entry.DosType] = loadedEntry;
                if (resource != 0)
                {
                    WriteFileSystemEntry(bus, allocator, resource, loadedEntry);
                }
            }

            return loaded;
        }

        private static uint EnsureFileSystemResource(AmigaBus bus, BootMetadataAllocator allocator)
        {
            var resourceList = allocator.ExecBase + ExecResourceListOffset;
            if (allocator.ExecBase == 0 || !bus.IsMappedMemoryRange(resourceList, 14))
            {
                return 0;
            }

            var existing = FindNodeByName(bus, resourceList, "FileSystem.resource");
            if (existing != 0)
            {
                return existing;
            }

            var resource = allocator.Allocate(FileSystemResourceSize);
            var name = allocator.Allocate("FileSystem.resource".Length + 1);
            var creator = allocator.Allocate(DeviceName.Length + 1);
            if (resource == 0 || name == 0 || creator == 0)
            {
                return 0;
            }

            bus.ClearMemory(resource, FileSystemResourceSize);
            WriteCString(bus, name, "FileSystem.resource");
            WriteCString(bus, creator, DeviceName);
            bus.WriteByte(resource + 0x08, NodeTypeResource, 0);
            bus.WriteLong(resource + NodeNameOffset, name);
            bus.WriteLong(resource + 0x0E, creator);
            InitializeList(bus, resource + FileSystemResourceEntriesOffset, 0);
            LinkTail(bus, resourceList, resource);
            return resource;
        }

        private static void WriteFileSystemEntry(
            AmigaBus bus,
            BootMetadataAllocator allocator,
            uint fileSystemResource,
            LoadedFileSystem loaded)
        {
            var entry = allocator.Allocate(FileSystemEntrySize);
            var name = allocator.Allocate(DeviceName.Length + 1);
            if (entry == 0 || name == 0)
            {
                return;
            }

            var fileSystem = loaded.FileSystem;
            bus.ClearMemory(entry, FileSystemEntrySize);
            WriteCString(bus, name, DeviceName);
            bus.WriteLong(entry + NodeNameOffset, name);
            bus.WriteLong(entry + 0x0E, fileSystem.DosType);
            bus.WriteLong(entry + 0x12, fileSystem.Version);
            bus.WriteLong(entry + 0x16, fileSystem.PatchFlags);
            bus.WriteLong(entry + 0x1A, fileSystem.NodeType);
            bus.WriteLong(entry + 0x1E, fileSystem.Task);
            bus.WriteLong(entry + 0x22, fileSystem.Lock);
            bus.WriteLong(entry + 0x26, fileSystem.Handler);
            bus.WriteLong(entry + 0x2A, fileSystem.StackSize);
            bus.WriteLong(entry + 0x2E, unchecked((uint)fileSystem.Priority));
            bus.WriteLong(entry + 0x32, fileSystem.Startup);
            bus.WriteLong(entry + 0x36, loaded.SegmentListBptr);
            bus.WriteLong(entry + 0x3A, fileSystem.GlobalVec);
            LinkTail(bus, fileSystemResource + FileSystemResourceEntriesOffset, entry);
        }

        private static uint FindNodeByName(AmigaBus bus, uint listAddress, string name)
        {
            if (listAddress == 0 || !bus.IsMappedMemoryRange(listAddress, 14))
            {
                return 0;
            }

            var node = bus.ReadLong(listAddress);
            for (var guard = 0; node != 0 && node != listAddress + 4 && guard < 128; guard++)
            {
                if (!bus.IsMappedMemoryRange(node, 14))
                {
                    return 0;
                }

                var nameAddress = bus.ReadLong(node + NodeNameOffset);
                if (StringEqualsCString(bus, nameAddress, name))
                {
                    return node;
                }

                node = bus.ReadLong(node);
            }

            return 0;
        }

        private static bool StringEqualsCString(AmigaBus bus, uint address, string value)
        {
            if (address == 0)
            {
                return false;
            }

            for (var i = 0; i < value.Length; i++)
            {
                if (!bus.IsMappedMemoryRange(address + (uint)i, 1) ||
                    bus.ReadByte(address + (uint)i) != (byte)value[i])
                {
                    return false;
                }
            }

            return bus.IsMappedMemoryRange(address + (uint)value.Length, 1) &&
                bus.ReadByte(address + (uint)value.Length) == 0;
        }

        private void RegisterDeviceTrap(AmigaBus bus, int displacement, Action<M68kCpuState> callback)
            => bus.RegisterHostTrapStub(unchecked((uint)((int)_deviceBase + displacement)), callback);

        private static void WriteUnit(AmigaBus bus, uint address)
        {
            bus.ClearMemory(address, 0x30);
            InitializeList(bus, address + UnitMessageListOffset, NodeTypeDevice);
        }

        private static void WriteBstr(AmigaBus bus, uint address, string value)
        {
            var length = Math.Min(value.Length, 255);
            bus.WriteByte(address, (byte)length, 0);
            for (var i = 0; i < length; i++)
            {
                bus.WriteByte(address + 1u + (uint)i, (byte)value[i], 0);
            }

            bus.WriteByte(address + 1u + (uint)length, 0, 0);
        }

        private static void WriteCString(AmigaBus bus, uint address, string value)
        {
            var length = Math.Min(value.Length, 255);
            for (var i = 0; i < length; i++)
            {
                bus.WriteByte(address + (uint)i, (byte)value[i], 0);
            }

            bus.WriteByte(address + (uint)length, 0, 0);
        }

        private static void WriteDosEnvec(AmigaBus bus, uint address, AmigaDosEnvec environment)
        {
            bus.ClearMemory(address, AmigaDosEnvec.LongCount * 4);
            for (var i = 0; i < AmigaDosEnvec.LongCount; i++)
            {
                bus.WriteLong(address + (uint)(i * 4), environment[i]);
            }
        }

        private static void WriteFileSysStartupMsg(AmigaBus bus, uint address, int unit, uint deviceNameBstr, uint envec)
        {
            bus.WriteLong(address + 0x00, (uint)unit);
            bus.WriteLong(address + 0x04, deviceNameBstr >> 2);
            bus.WriteLong(address + 0x08, envec >> 2);
            bus.WriteLong(address + 0x0C, 0);
        }

        private static void WriteDeviceNode(AmigaBus bus, uint address, uint startup, uint dosNameBstr, LoadedFileSystem? fileSystem)
        {
            bus.ClearMemory(address, 0x2C);
            bus.WriteLong(address + DeviceNodeTypeOffset, 0);
            bus.WriteLong(address + DeviceNodeLockOffset, 0xFFFF_FFFF);
            bus.WriteLong(address + DeviceNodeStartupOffset, startup >> 2);
            bus.WriteLong(address + DeviceNodeNameOffset, dosNameBstr >> 2);
            if (fileSystem == null)
            {
                return;
            }

            var patchFlags = fileSystem.FileSystem.PatchFlags;
            if ((patchFlags & FileSystemPatchType) != 0)
            {
                bus.WriteLong(address + DeviceNodeTypeOffset, fileSystem.FileSystem.NodeType);
            }

            if ((patchFlags & FileSystemPatchTask) != 0)
            {
                bus.WriteLong(address + DeviceNodeTaskOffset, fileSystem.FileSystem.Task);
            }

            if ((patchFlags & FileSystemPatchLock) != 0)
            {
                bus.WriteLong(address + DeviceNodeLockOffset, fileSystem.FileSystem.Lock);
            }

            if ((patchFlags & FileSystemPatchHandler) != 0)
            {
                bus.WriteLong(address + DeviceNodeHandlerOffset, fileSystem.FileSystem.Handler);
            }

            if ((patchFlags & FileSystemPatchStackSize) != 0)
            {
                bus.WriteLong(address + DeviceNodeStackSizeOffset, fileSystem.FileSystem.StackSize);
            }

            if ((patchFlags & FileSystemPatchPriority) != 0)
            {
                bus.WriteLong(address + DeviceNodePriorityOffset, unchecked((uint)fileSystem.FileSystem.Priority));
            }

            if ((patchFlags & FileSystemPatchStartup) != 0 && fileSystem.FileSystem.Startup != 0)
            {
                bus.WriteLong(address + DeviceNodeStartupOffset, fileSystem.FileSystem.Startup);
            }

            if ((patchFlags & FileSystemPatchSegList) != 0)
            {
                bus.WriteLong(address + DeviceNodeSegListOffset, fileSystem.SegmentListBptr);
            }

            if ((patchFlags & FileSystemPatchGlobalVec) != 0)
            {
                bus.WriteLong(address + DeviceNodeGlobalVecOffset, fileSystem.FileSystem.GlobalVec);
            }
        }

        private static void WriteBootNode(AmigaBus bus, uint address, uint deviceNode, uint dosName, int bootPri)
        {
            bus.ClearMemory(address, 0x20);
            bus.WriteByte(address + 0x08, NodeTypeBootNode, 0);
            bus.WriteByte(address + 0x09, unchecked((byte)bootPri), 0);
            bus.WriteLong(address + 0x0A, dosName);
            bus.WriteWord(address + 0x0E, 0);
            bus.WriteLong(address + 0x10, deviceNode);
        }

        private static void LinkTail(AmigaBus bus, uint listAddress, uint nodeAddress)
        {
            if (listAddress == 0 || nodeAddress == 0 || !bus.IsMappedMemoryRange(listAddress, 14))
            {
                return;
            }

            if (bus.ReadLong(listAddress) == 0 && bus.ReadLong(listAddress + 8) == 0)
            {
                InitializeList(bus, listAddress, 0);
            }

            var tailPred = bus.ReadLong(listAddress + 8);
            if (tailPred == 0)
            {
                InitializeList(bus, listAddress, 0);
                tailPred = listAddress;
            }

            bus.WriteLong(nodeAddress + 0x00, listAddress + 4);
            bus.WriteLong(nodeAddress + 0x04, tailPred);
            bus.WriteLong(tailPred == listAddress ? listAddress : tailPred, nodeAddress);
            bus.WriteLong(listAddress + 8, nodeAddress);
        }

        private static void InitializeList(AmigaBus bus, uint listAddress, byte type)
        {
            bus.WriteLong(listAddress + 0x00, listAddress + 4);
            bus.WriteLong(listAddress + 0x04, 0);
            bus.WriteLong(listAddress + 0x08, listAddress);
            bus.WriteByte(listAddress + 0x0C, type, 0);
            bus.WriteByte(listAddress + 0x0D, 0, 0);
        }

        private void HostDeviceOpen(M68kCpuState state)
        {
            var unit = checked((int)state.D[0]);
            var ioRequest = state.A[1];
            if (!_hardfiles.ContainsKey(unit) || !_unitAddresses.TryGetValue(unit, out var unitAddress))
            {
                if (ioRequest != 0)
                {
                    _bootstrapBus?.WriteByte(ioRequest + IoErrorOffset, IoErrOpenFail, 0);
                }

                state.D[0] = IoErrOpenFail;
                return;
            }

            var bus = _bootstrapBus;
            if (bus != null && ioRequest != 0 && bus.IsMappedMemoryRange(ioRequest, 0x20))
            {
                bus.WriteLong(ioRequest + IoDeviceOffset, _deviceBase);
                bus.WriteLong(ioRequest + IoUnitOffsetInRequest, unitAddress);
                bus.WriteByte(ioRequest + IoErrorOffset, 0, 0);
                var openCount = bus.ReadWord(_deviceBase + 0x20);
                bus.WriteWord(_deviceBase + 0x20, (ushort)(openCount + 1));
                var unitOpenCount = bus.ReadWord(unitAddress + UnitOpenCountOffset);
                bus.WriteWord(unitAddress + UnitOpenCountOffset, (ushort)(unitOpenCount + 1));
            }

            state.D[0] = 0;
        }

        private void HostDeviceClose(M68kCpuState state)
        {
            var bus = _bootstrapBus;
            if (bus != null && _deviceBase != 0)
            {
                var openCount = bus.ReadWord(_deviceBase + 0x20);
                if (openCount != 0)
                {
                    bus.WriteWord(_deviceBase + 0x20, (ushort)(openCount - 1));
                }
            }

            state.D[0] = 0;
        }

        private void HostDeviceExpunge(M68kCpuState state)
            => state.D[0] = 0;

        private void HostDeviceExtFunc(M68kCpuState state)
            => state.D[0] = 0;

        private void HostDeviceBeginIo(M68kCpuState state)
        {
            var bus = _bootstrapBus;
            var ioRequest = state.A[1];
            if (bus == null || ioRequest == 0 || !bus.IsMappedMemoryRange(ioRequest, 0x30))
            {
                return;
            }

            var flags = bus.ReadByte(ioRequest + IoFlagsOffset);
            bus.WriteByte(ioRequest + IoFlagsOffset, (byte)(flags | 0x01), 0);
            if (!TryExecuteIoRequest(bus, ioRequest))
            {
                CompleteIo(bus, ioRequest, IoErrBadAddress, 0);
            }
        }

        private void HostDeviceAbortIo(M68kCpuState state)
        {
            var bus = _bootstrapBus;
            var ioRequest = state.A[1];
            if (bus != null && ioRequest != 0 && bus.IsMappedMemoryRange(ioRequest, 0x20))
            {
                bus.WriteByte(ioRequest + IoErrorOffset, 0, 0);
            }

            state.D[0] = 0;
        }

        private int ReadUnit(AmigaBus bus, uint ioRequestAddress)
        {
            var unitAddress = bus.ReadLong(ioRequestAddress + IoUnitOffset);
            foreach (var entry in _unitAddresses)
            {
                if (entry.Value == unitAddress)
                {
                    return entry.Key;
                }
            }

            if (unitAddress == 0 || !bus.IsMappedMemoryRange(unitAddress, 4))
            {
                return 0;
            }

            return checked((int)bus.ReadLong(unitAddress));
        }

        private static void ExecuteRead(AmigaBus bus, uint ioRequestAddress, AmigaHardfile hardfile)
            => ExecuteTransfer(bus, ioRequestAddress, hardfile, isWrite: false, ReadOffset32(bus, ioRequestAddress));

        private static void ExecuteWrite(AmigaBus bus, uint ioRequestAddress, AmigaHardfile hardfile)
            => ExecuteTransfer(bus, ioRequestAddress, hardfile, isWrite: true, ReadOffset32(bus, ioRequestAddress));

        private static void ExecuteRead64(AmigaBus bus, uint ioRequestAddress, AmigaHardfile hardfile)
            => ExecuteTransfer(bus, ioRequestAddress, hardfile, isWrite: false, ReadOffset64(bus, ioRequestAddress));

        private static void ExecuteWrite64(AmigaBus bus, uint ioRequestAddress, AmigaHardfile hardfile)
            => ExecuteTransfer(bus, ioRequestAddress, hardfile, isWrite: true, ReadOffset64(bus, ioRequestAddress));

        private static void ExecuteSeek64(AmigaBus bus, uint ioRequestAddress, AmigaHardfile hardfile)
        {
            var offset = ReadOffset64(bus, ioRequestAddress);
            if ((offset % AmigaHardfile.SectorSize) != 0 || offset > (ulong)hardfile.Length)
            {
                CompleteIo(bus, ioRequestAddress, IoErrBadLength, 0);
                return;
            }

            CompleteIo(bus, ioRequestAddress, 0, 0);
        }

        private static ulong ReadOffset32(AmigaBus bus, uint ioRequestAddress)
            => bus.ReadLong(ioRequestAddress + IoOffsetOffset);

        private static ulong ReadOffset64(AmigaBus bus, uint ioRequestAddress)
            => ((ulong)bus.ReadLong(ioRequestAddress + IoActualOffset) << 32) |
                bus.ReadLong(ioRequestAddress + IoOffsetOffset);

        private static void ExecuteTransfer(AmigaBus bus, uint ioRequestAddress, AmigaHardfile hardfile, bool isWrite, ulong byteOffset)
        {
            var lengthValue = bus.ReadLong(ioRequestAddress + IoLengthOffset);
            if (lengthValue > int.MaxValue)
            {
                CompleteIo(bus, ioRequestAddress, IoErrBadLength, 0);
                return;
            }

            var length = (int)lengthValue;
            var dataAddress = bus.ReadLong(ioRequestAddress + IoDataOffset);
            if ((length % AmigaHardfile.SectorSize) != 0 ||
                (byteOffset % AmigaHardfile.SectorSize) != 0 ||
                byteOffset > long.MaxValue ||
                !bus.IsMappedMemoryRange(dataAddress, length))
            {
                CompleteIo(bus, ioRequestAddress, IoErrBadLength, 0);
                return;
            }

            var buffer = new byte[length];
            if (isWrite)
            {
                bus.CopyFromMemory(dataAddress, buffer);
                hardfile.Write((long)byteOffset, buffer);
            }
            else
            {
                hardfile.Read((long)byteOffset, buffer);
                bus.CopyToMemory(dataAddress, buffer);
            }

            CompleteIo(bus, ioRequestAddress, 0, (uint)length);
        }

        private static void ExecuteDeviceQuery(AmigaBus bus, uint ioRequestAddress)
        {
            var dataAddress = bus.ReadLong(ioRequestAddress + IoDataOffset);
            var length = checked((int)bus.ReadLong(ioRequestAddress + IoLengthOffset));
            if (length < 16 || !bus.IsMappedMemoryRange(dataAddress, length))
            {
                CompleteIo(bus, ioRequestAddress, IoErrBadLength, 0);
                return;
            }

            bus.WriteLong(dataAddress, 0);
            bus.WriteLong(dataAddress + 4, 16);
            bus.WriteWord(dataAddress + 8, 0);
            bus.WriteWord(dataAddress + 10, AmigaHardfile.SectorSize);
            bus.WriteLong(dataAddress + 12, 0);
            CompleteIo(bus, ioRequestAddress, 0, 16);
        }

        private static void ExecuteScsiCommand(AmigaBus bus, uint ioRequestAddress, AmigaHardfile hardfile)
        {
            var scsiIoAddress = bus.ReadLong(ioRequestAddress + IoDataOffset);
            if (scsiIoAddress == 0 || !bus.IsMappedMemoryRange(scsiIoAddress, 0x28))
            {
                CompleteIo(bus, ioRequestAddress, IoErrBadAddress, 0);
                return;
            }

            var dataAddress = bus.ReadLong(scsiIoAddress + 0x00);
            var dataLength = checked((int)bus.ReadLong(scsiIoAddress + 0x04));
            var commandAddress = bus.ReadLong(scsiIoAddress + 0x0C);
            var commandLength = checked((int)bus.ReadWord(scsiIoAddress + 0x10));
            if (commandLength <= 0 || !bus.IsMappedMemoryRange(commandAddress, commandLength))
            {
                CompleteIo(bus, ioRequestAddress, IoErrBadAddress, 0);
                return;
            }

            var opcode = bus.ReadByte(commandAddress);
            switch (opcode)
            {
                case 0x00: // TEST UNIT READY
                    bus.WriteLong(scsiIoAddress + 0x08, 0);
                    CompleteIo(bus, ioRequestAddress, 0, 0);
                    return;
                case 0x12: // INQUIRY
                    WriteScsiInquiry(bus, dataAddress, dataLength);
                    bus.WriteLong(scsiIoAddress + 0x08, (uint)Math.Min(dataLength, 36));
                    CompleteIo(bus, ioRequestAddress, 0, 0);
                    return;
                case 0x25: // READ CAPACITY(10)
                    WriteReadCapacity(bus, dataAddress, dataLength, hardfile);
                    bus.WriteLong(scsiIoAddress + 0x08, 8);
                    CompleteIo(bus, ioRequestAddress, 0, 0);
                    return;
                default:
                    CompleteIo(bus, ioRequestAddress, IoErrUnsupported, 0);
                    return;
            }
        }

        private static void WriteScsiInquiry(AmigaBus bus, uint dataAddress, int dataLength)
        {
            if (dataLength <= 0 || !bus.IsMappedMemoryRange(dataAddress, dataLength))
            {
                return;
            }

            var buffer = new byte[Math.Min(dataLength, 36)];
            buffer[0] = 0;
            buffer[2] = 2;
            buffer[4] = 31;
            WriteAscii(buffer, 8, 8, "COPPER");
            WriteAscii(buffer, 16, 16, "CopperHDF");
            WriteAscii(buffer, 32, 4, "0001");
            bus.CopyToMemory(dataAddress, buffer);
        }

        private static void WriteReadCapacity(AmigaBus bus, uint dataAddress, int dataLength, AmigaHardfile hardfile)
        {
            if (dataLength < 8 || !bus.IsMappedMemoryRange(dataAddress, dataLength))
            {
                return;
            }

            var lastBlock = hardfile.SectorCount == 0
                ? 0
                : Math.Min(uint.MaxValue, hardfile.SectorCount - 1);
            bus.WriteLong(dataAddress, (uint)lastBlock);
            bus.WriteLong(dataAddress + 4, AmigaHardfile.SectorSize);
        }

        private static void CompleteIo(AmigaBus bus, uint ioRequestAddress, byte error, uint actual)
        {
            bus.WriteByte(ioRequestAddress + IoErrorOffset, error, 0);
            bus.WriteLong(ioRequestAddress + IoActualOffset, actual);
        }

        private static void WriteAscii(byte[] buffer, int offset, int length, string value)
        {
            for (var i = 0; i < length; i++)
            {
                buffer[offset + i] = i < value.Length ? (byte)value[i] : (byte)' ';
            }
        }

        private static bool TryLoadHunkSegmentList(
            AmigaBus bus,
            BootMetadataAllocator allocator,
            ReadOnlySpan<byte> data,
            out uint segmentListBptr)
        {
            const uint hunkUnit = 0x0000_03E7;
            const uint hunkName = 0x0000_03E8;
            const uint hunkCode = 0x0000_03E9;
            const uint hunkData = 0x0000_03EA;
            const uint hunkBss = 0x0000_03EB;
            const uint hunkReloc32 = 0x0000_03EC;
            const uint hunkSymbol = 0x0000_03F0;
            const uint hunkDebug = 0x0000_03F1;
            const uint hunkEnd = 0x0000_03F2;
            const uint hunkHeader = 0x0000_03F3;
            const uint hunkIdMask = 0x3FFF_FFFF;

            segmentListBptr = 0;
            try
            {
                static uint Normalize(uint value, uint mask)
                    => value & mask;

                var reader = new HunkReader(data);
                if (Normalize(reader.ReadUInt32("hunk header"), hunkIdMask) != hunkHeader)
                {
                    return false;
                }

                while (true)
                {
                    var nameLength = CheckedHunkInt(reader.ReadUInt32("resident library name length"));
                    if (nameLength == 0)
                    {
                        break;
                    }

                    reader.Skip(checked(nameLength * 4), "resident library name");
                }

                var tableSize = CheckedHunkInt(reader.ReadUInt32("hunk table size"));
                var firstHunk = CheckedHunkInt(reader.ReadUInt32("first hunk"));
                var lastHunk = CheckedHunkInt(reader.ReadUInt32("last hunk"));
                if (tableSize <= 0 || firstHunk < 0 || lastHunk < firstHunk)
                {
                    return false;
                }

                var hunkCount = lastHunk - firstHunk + 1;
                if (hunkCount > tableSize)
                {
                    return false;
                }

                var allocations = new uint[hunkCount];
                var bases = new uint[hunkCount];
                var declaredSizes = new int[hunkCount];
                for (var i = 0; i < tableSize; i++)
                {
                    var sizeWord = reader.ReadUInt32("hunk memory size");
                    if (i >= hunkCount)
                    {
                        continue;
                    }

                    declaredSizes[i] = checked(CheckedHunkInt(sizeWord & hunkIdMask) * 4);
                    allocations[i] = allocator.Allocate(Math.Max(4, declaredSizes[i]) + 4);
                    if (allocations[i] == 0)
                    {
                        return false;
                    }

                    bus.ClearMemory(allocations[i], Math.Max(4, declaredSizes[i]) + 4);
                    bases[i] = allocations[i] + 4;
                }

                for (var i = 0; i < hunkCount; i++)
                {
                    bus.WriteLong(allocations[i], i + 1 < hunkCount ? allocations[i + 1] >> 2 : 0);
                }

                var segmentIndex = 0;
                while (!reader.EndOfData && segmentIndex < hunkCount)
                {
                    var type = Normalize(reader.ReadUInt32("hunk section"), hunkIdMask);
                    if (type == hunkUnit || type == hunkName)
                    {
                        var length = CheckedHunkInt(reader.ReadUInt32("hunk string length"));
                        reader.Skip(checked(length * 4), "hunk string");
                        continue;
                    }

                    if (type != hunkCode && type != hunkData && type != hunkBss)
                    {
                        return false;
                    }

                    var sourceSegment = segmentIndex++;
                    if (type == hunkBss)
                    {
                        _ = reader.ReadUInt32("BSS size");
                    }
                    else
                    {
                        var dataBytes = checked(CheckedHunkInt(reader.ReadUInt32("segment data size")) * 4);
                        if (dataBytes > declaredSizes[sourceSegment])
                        {
                            return false;
                        }

                        bus.CopyToMemory(bases[sourceSegment], reader.ReadBytes(dataBytes, "segment data"));
                    }

                    while (true)
                    {
                        if (reader.EndOfData)
                        {
                            return false;
                        }

                        var subType = Normalize(reader.ReadUInt32("hunk subsection"), hunkIdMask);
                        if (subType == hunkEnd)
                        {
                            break;
                        }

                        switch (subType)
                        {
                            case hunkReloc32:
                                while (true)
                                {
                                    var count = CheckedHunkInt(reader.ReadUInt32("relocation count"));
                                    if (count == 0)
                                    {
                                        break;
                                    }

                                    var target = CheckedHunkInt(reader.ReadUInt32("relocation target"));
                                    if (target < 0 || target >= bases.Length)
                                    {
                                        return false;
                                    }

                                    for (var i = 0; i < count; i++)
                                    {
                                        var offset = CheckedHunkInt(reader.ReadUInt32("relocation offset"));
                                        var address = bases[sourceSegment] + (uint)offset;
                                        bus.WriteLong(address, bus.ReadLong(address) + bases[target]);
                                    }
                                }

                                break;
                            case hunkSymbol:
                                while (true)
                                {
                                    var length = CheckedHunkInt(reader.ReadUInt32("symbol length"));
                                    if (length == 0)
                                    {
                                        break;
                                    }

                                    reader.Skip(checked(length * 4), "symbol name");
                                    reader.Skip(4, "symbol value");
                                }

                                break;
                            case hunkDebug:
                                var debugLength = CheckedHunkInt(reader.ReadUInt32("debug length"));
                                reader.Skip(checked(debugLength * 4), "debug data");
                                break;
                            default:
                                return false;
                        }
                    }
                }

                if (segmentIndex == 0)
                {
                    return false;
                }

                segmentListBptr = allocations[0] >> 2;
                return true;
            }
            catch (AmigaEmulationException)
            {
                segmentListBptr = 0;
                return false;
            }
            catch (OverflowException)
            {
                segmentListBptr = 0;
                return false;
            }
        }

        private static int CheckedHunkInt(uint value)
        {
            if (value > int.MaxValue)
            {
                throw new AmigaEmulationException("The HUNK field is too large.");
            }

            return (int)value;
        }

        private sealed class LoadedFileSystem
        {
            public LoadedFileSystem(AmigaRdbFileSystem fileSystem, uint segmentListBptr)
            {
                FileSystem = fileSystem;
                SegmentListBptr = segmentListBptr;
            }

            public AmigaRdbFileSystem FileSystem { get; }

            public uint SegmentListBptr { get; }
        }

        private sealed class BootMetadataAllocator
        {
            private readonly AmigaBus _bus;
            private readonly uint _copyEnd;
            private uint _copyCursor;

            public BootMetadataAllocator(AmigaBus bus, uint copyBase, uint execBase)
            {
                _bus = bus;
                ExecBase = execBase;
                _copyCursor = copyBase + PerUnitDataOffset;
                _copyEnd = copyBase != 0 && bus.IsMappedMemoryRange(copyBase, DiagAreaCopySize)
                    ? copyBase + DiagAreaCopySize
                    : 0;
            }

            public uint ExecBase { get; }

            public uint Allocate(int byteCount)
            {
                if (byteCount <= 0)
                {
                    return 0;
                }

                var size = Align((uint)byteCount, 8);
                if (_copyEnd != 0 && _copyCursor + size > _copyCursor && _copyCursor + size <= _copyEnd)
                {
                    var address = _copyCursor;
                    _copyCursor += size;
                    _bus.ClearMemory(address, checked((int)size));
                    return address;
                }

                return AllocatePublic(byteCount, clear: true);
            }

            private uint AllocatePublic(int byteCount, bool clear)
            {
                if (ExecBase == 0 || byteCount <= 0)
                {
                    return 0;
                }

                var listAddress = ExecBase + ExecMemListOffset;
                if (!_bus.IsMappedMemoryRange(listAddress, 14))
                {
                    return 0;
                }

                var size = Align((uint)byteCount, 8);
                var headerAddress = _bus.ReadLong(listAddress);
                for (var headerGuard = 0; headerAddress != 0 && headerAddress != listAddress + 4 && headerGuard < 16; headerGuard++)
                {
                    if (!IsPublicMemoryHeader(headerAddress))
                    {
                        headerAddress = _bus.IsMappedMemoryRange(headerAddress, 4) ? _bus.ReadLong(headerAddress) : 0;
                        continue;
                    }

                    var previousLinkAddress = headerAddress + MemHeaderFirstChunkOffset;
                    var chunkAddress = _bus.ReadLong(previousLinkAddress);
                    for (var chunkGuard = 0; chunkAddress != 0 && chunkGuard < 1024; chunkGuard++)
                    {
                        if (!_bus.IsMappedMemoryRange(chunkAddress, 8))
                        {
                            break;
                        }

                        var nextChunkAddress = _bus.ReadLong(chunkAddress + MemChunkNextOffset);
                        var chunkBytes = _bus.ReadLong(chunkAddress + MemChunkBytesOffset);
                        if (chunkBytes >= size)
                        {
                            var allocatedAddress = chunkAddress;
                            uint allocatedBytes;
                            if (chunkBytes - size < 8)
                            {
                                allocatedBytes = chunkBytes;
                                _bus.WriteLong(previousLinkAddress, nextChunkAddress);
                            }
                            else
                            {
                                allocatedBytes = size;
                                var remainingChunkAddress = chunkAddress + size;
                                _bus.WriteLong(previousLinkAddress, remainingChunkAddress);
                                _bus.WriteLong(remainingChunkAddress + MemChunkNextOffset, nextChunkAddress);
                                _bus.WriteLong(remainingChunkAddress + MemChunkBytesOffset, chunkBytes - size);
                            }

                            var freeBytes = _bus.ReadLong(headerAddress + MemHeaderFreeOffset);
                            _bus.WriteLong(headerAddress + MemHeaderFreeOffset, freeBytes >= allocatedBytes ? freeBytes - allocatedBytes : 0);
                            if (clear)
                            {
                                _bus.ClearMemory(allocatedAddress, checked((int)allocatedBytes));
                            }

                            return allocatedAddress;
                        }

                        previousLinkAddress = chunkAddress + MemChunkNextOffset;
                        chunkAddress = nextChunkAddress;
                    }

                    headerAddress = _bus.ReadLong(headerAddress);
                }

                return 0;
            }

            private bool IsPublicMemoryHeader(uint headerAddress)
            {
                if (!_bus.IsMappedMemoryRange(headerAddress, 0x20))
                {
                    return false;
                }

                return ((uint)_bus.ReadWord(headerAddress + MemHeaderAttributesOffset) & MemfPublic) != 0;
            }

            private static uint Align(uint value, uint alignment)
                => (value + alignment - 1u) & ~(alignment - 1u);
        }

        private ref struct HunkReader
        {
            private readonly ReadOnlySpan<byte> _data;
            private int _offset;

            public HunkReader(ReadOnlySpan<byte> data)
            {
                _data = data;
                _offset = 0;
            }

            public bool EndOfData => _offset >= _data.Length;

            public uint ReadUInt32(string fieldName)
            {
                if (_offset + 4 > _data.Length)
                {
                    throw new AmigaEmulationException($"Unexpected end of HUNK data while reading {fieldName}.");
                }

                var value = BigEndian.ReadUInt32(_data, _offset, fieldName);
                _offset += 4;
                return value;
            }

            public ReadOnlySpan<byte> ReadBytes(int count, string fieldName)
            {
                if (count < 0 || _offset + count > _data.Length)
                {
                    throw new AmigaEmulationException($"Unexpected end of HUNK data while reading {fieldName}.");
                }

                var value = _data.Slice(_offset, count);
                _offset += count;
                return value;
            }

            public void Skip(int count, string fieldName)
                => _ = ReadBytes(count, fieldName);
        }

        private static byte[] CreateBoardRom()
        {
            var rom = new byte[BoardSize];
            WriteDiagArea(rom);
            WriteResident(rom);
            WriteReturnStub(rom, DiagPointOffset);
            WriteReturnStub(rom, BootPointOffset);
            WriteReturnStub(rom, ResidentInitOffset);
            WriteNullTerminatedAscii(rom, 0x100, DeviceName);
            WriteNullTerminatedAscii(rom, DiagAreaOffset + NameOffset, DeviceName);
            WriteNullTerminatedAscii(rom, DiagAreaOffset + IdStringOffset, $"{DeviceName} 0.1");
            return rom;
        }

        private void WriteTrap(int relativeOffset, ushort trapId)
        {
            var offset = DiagAreaOffset + relativeOffset;
            WriteUInt16(_boardRom, offset, 0xFF00);
            WriteUInt16(_boardRom, offset + 2, trapId);
        }

        private static void WriteDiagArea(byte[] rom)
        {
            var offset = DiagAreaOffset;
            rom[offset] = 0x90; // DAC_WORDWIDE | DAC_CONFIGTIME.
            rom[offset + 1] = 0;
            WriteUInt16(rom, offset + 0x02, DiagAreaCopySize);
            WriteUInt16(rom, offset + 0x04, DiagPointOffset);
            WriteUInt16(rom, offset + 0x06, BootPointOffset);
            WriteUInt16(rom, offset + 0x08, NameOffset);
            WriteUInt16(rom, offset + 0x0A, 0);
            WriteUInt16(rom, offset + 0x0C, 0);
        }

        private static void WriteResident(byte[] rom)
        {
            var offset = DiagAreaOffset + ResidentOffset;
            WriteUInt16(rom, offset, 0x4AFC);
            WriteUInt32(rom, offset + 0x02, ResidentOffset);
            WriteUInt32(rom, offset + 0x06, ResidentInitOffset + 4u);
            rom[offset + 0x0A] = 0x01;
            rom[offset + 0x0B] = 0x01;
            rom[offset + 0x0C] = 0x03;
            rom[offset + 0x0D] = 20;
            WriteUInt32(rom, offset + 0x0E, NameOffset);
            WriteUInt32(rom, offset + 0x12, IdStringOffset);
            WriteUInt32(rom, offset + 0x16, ResidentInitOffset);
        }

        private static void WriteReturnStub(byte[] rom, int relativeOffset)
        {
            var offset = DiagAreaOffset + relativeOffset;
            rom[offset] = 0x70; // moveq #1,d0
            rom[offset + 1] = 0x01;
            rom[offset + 2] = 0x4E; // rts
            rom[offset + 3] = 0x75;
        }

        private static void WriteNullTerminatedAscii(byte[] buffer, int offset, string value)
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(value);
            Array.Copy(bytes, 0, buffer, offset, bytes.Length);
            buffer[offset + bytes.Length] = 0;
        }

        private static void WriteUInt16(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)(value >> 8);
            buffer[offset + 1] = (byte)value;
        }

        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }
    }
}
