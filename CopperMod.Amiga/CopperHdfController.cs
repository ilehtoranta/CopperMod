using System;
using System.Collections.Generic;

namespace CopperMod.Amiga
{
    internal sealed class CopperHdfController : IDisposable
    {
        public const string DeviceName = "copperhdf.device";
        public const uint AutoConfigBase = 0x00E8_0000;
        public const uint AutoConfigSize = 0x0001_0000;
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
        private const uint DosTypeOFS = 0x444F_5300;
        private const int ExecDeviceListOffset = 0x015E;
        private const int ExpansionMountListOffset = 0x004A;
        private const int ConfigDevFlagsOffset = 0x0E;
        private const int ConfigDevDriverOffset = 0x28;
        private const byte ConfigDevConfigMeFlag = 0x02;
        private const byte NodeTypeDevice = 3;
        private const byte NodeTypeBootNode = 16;
        private const int LibrarySize = 0x22;
        private const int DeviceTrapVectorSize = 6 * 6;
        private const int PerUnitDataSize = 0x0100;
        private const int UnitMessageListOffset = 0x14;
        private const int UnitOpenCountOffset = 0x24;

        private readonly Dictionary<int, AmigaHardfile> _hardfiles = new Dictionary<int, AmigaHardfile>();
        private readonly byte[] _boardRom;
        private uint _configuredBase;
        private uint _pendingBase;
        private bool _shutUp;
        private AmigaBus? _bootstrapBus;
        private bool _bootstrapInstalled;
        private uint _diagCopyBase;
        private uint _execBase;
        private uint _expansionBase;
        private uint _configDev;
        private uint _deviceBase;
        private readonly Dictionary<int, uint> _unitAddresses = new Dictionary<int, uint>();

        public CopperHdfController(IEnumerable<AmigaHardfileConfiguration> configurations)
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

        public bool IsPresent => _hardfiles.Count != 0;

        public bool IsConfigured => _configuredBase != 0;

        public uint ConfiguredBase => _configuredBase;

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

        public bool ContainsAutoConfigAddress(uint address)
        {
            address &= 0x00FF_FFFF;
            return IsPresent &&
                !IsConfigured &&
                !_shutUp &&
                address >= AutoConfigBase &&
                address < AutoConfigBase + AutoConfigSize;
        }

        public bool ContainsBoardAddress(uint address)
        {
            address &= 0x00FF_FFFF;
            return IsPresent &&
                IsConfigured &&
                address >= _configuredBase &&
                address < _configuredBase + BoardSize;
        }

        public byte ReadAutoConfigByte(uint address)
        {
            var offset = (int)((address - AutoConfigBase) & 0xFF);
            var value = ReadAutoConfigNibble(offset);
            value <<= 4;
            if (offset != 0 && offset != 2 && offset != 0x40 && offset != 0x42)
            {
                value ^= 0xFF;
            }

            return unchecked((byte)value);
        }

        public void WriteAutoConfigByte(uint address, byte value)
        {
            var offset = (int)((address - AutoConfigBase) & 0xFF);
            switch (offset)
            {
                case 0x48:
                    _pendingBase = (_pendingBase & 0x000F_FFFFu) | ((uint)(value & 0xF0) << 16);
                    break;
                case 0x4A:
                    _pendingBase = (_pendingBase & 0x00F0_FFFFu) | ((uint)(value & 0xF0) << 12);
                    Configure(_pendingBase);
                    break;
                case 0x4C:
                    _shutUp = true;
                    break;
            }
        }

        public byte ReadBoardByte(uint address)
        {
            var offset = (int)((address - _configuredBase) & (BoardSize - 1));
            return _boardRom[offset];
        }

        public bool TryWriteBoardByte(uint address, byte value)
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
            catch (AmigaEmulationException)
            {
                CompleteIo(bus, ioRequestAddress, IoErrBadAddress, 0);
                return true;
            }
        }

        public void Reset()
        {
            _configuredBase = 0;
            _pendingBase = 0;
            _shutUp = false;
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

        private void Configure(uint baseAddress)
        {
            baseAddress &= 0x00FF_0000;
            if (baseAddress is >= 0x0008_0000 and < 0x0010_0000)
            {
                baseAddress |= 0x00E0_0000;
            }

            if (baseAddress == 0)
            {
                _shutUp = true;
                return;
            }

            _configuredBase = baseAddress;
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
            RegisterBootNodes(bus, copyBase, expansionBase);
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

        private void RegisterBootNodes(AmigaBus bus, uint copyBase, uint expansionBase)
        {
            if (BootNodeRegistered)
            {
                return;
            }

            var unitIndex = 0;
            foreach (var hardfile in _hardfiles.Values)
            {
                var unitBase = copyBase + PerUnitDataOffset + (uint)(unitIndex * PerUnitDataSize);
                var unitAddress = unitBase;
                var dosNode = unitBase + (DosNodeOffset - PerUnitDataOffset);
                var startup = unitBase + (FileSysStartupMsgOffset - PerUnitDataOffset);
                var envec = unitBase + (DosEnvecOffset - PerUnitDataOffset);
                var bootNode = unitBase + (BootNodeOffset - PerUnitDataOffset);
                var dosName = unitBase + (DosDeviceNameOffset - PerUnitDataOffset);
                var deviceNameBstr = unitBase + (ExecDeviceNameBstrOffset - PerUnitDataOffset);
                var bootPri = unitIndex == 0 ? 0 : -5;

                _unitAddresses[hardfile.Unit] = unitAddress;
                WriteUnit(bus, unitAddress);
                WriteDosDeviceName(bus, dosName, unitIndex);
                WriteBstr(bus, deviceNameBstr, DeviceName);
                WriteDosEnvec(bus, envec, hardfile, bootPri);
                WriteFileSysStartupMsg(bus, startup, hardfile.Unit, deviceNameBstr, envec);
                WriteDeviceNode(bus, dosNode, startup, dosName);
                WriteBootNode(bus, bootNode, dosNode, dosName, bootPri);
                if (unitIndex == 0)
                {
                    DeviceNodeAddress = dosNode;
                    BootNodeAddress = bootNode;
                }

                if (expansionBase != 0 && bus.IsMappedMemoryRange(expansionBase + ExpansionMountListOffset, 14))
                {
                    LinkTail(bus, expansionBase + ExpansionMountListOffset, bootNode);
                    BootNodeRegistered = true;
                }

                unitIndex++;
            }
        }

        private void RegisterDeviceTrap(AmigaBus bus, int displacement, Action<M68kCpuState> callback)
            => bus.RegisterHostTrapStub(unchecked((uint)((int)_deviceBase + displacement)), callback);

        private static void WriteUnit(AmigaBus bus, uint address)
        {
            bus.ClearMemory(address, 0x30);
            InitializeList(bus, address + UnitMessageListOffset, NodeTypeDevice);
        }

        private static void WriteDosDeviceName(AmigaBus bus, uint address, int index)
        {
            bus.WriteByte(address, (byte)'D', 0);
            bus.WriteByte(address + 1, (byte)'H', 0);
            bus.WriteByte(address + 2, (byte)('0' + Math.Min(index, 9)), 0);
            bus.WriteByte(address + 3, 0, 0);
            bus.WriteByte(address + 4, 3, 0);
            bus.WriteByte(address + 5, (byte)'D', 0);
            bus.WriteByte(address + 6, (byte)'H', 0);
            bus.WriteByte(address + 7, (byte)('0' + Math.Min(index, 9)), 0);
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

        private static void WriteDosEnvec(AmigaBus bus, uint address, AmigaHardfile hardfile, int bootPri)
        {
            var sectors = (uint)Math.Max(1, Math.Min(uint.MaxValue, hardfile.SectorCount));
            const uint heads = 1;
            const uint sectorsPerTrack = 32;
            var cylinders = Math.Max(1u, (sectors + sectorsPerTrack - 1) / sectorsPerTrack);

            bus.ClearMemory(address, 0x50);
            bus.WriteLong(address + 0x00, 16);
            bus.WriteLong(address + 0x04, AmigaHardfile.SectorSize / 4u);
            bus.WriteLong(address + 0x08, 0);
            bus.WriteLong(address + 0x0C, heads);
            bus.WriteLong(address + 0x10, 1);
            bus.WriteLong(address + 0x14, sectorsPerTrack);
            bus.WriteLong(address + 0x18, 2);
            bus.WriteLong(address + 0x1C, 0);
            bus.WriteLong(address + 0x20, 0);
            bus.WriteLong(address + 0x24, 0);
            bus.WriteLong(address + 0x28, cylinders - 1);
            bus.WriteLong(address + 0x2C, 30);
            bus.WriteLong(address + 0x30, MemfPublic);
            bus.WriteLong(address + 0x34, 0x0020_0000);
            bus.WriteLong(address + 0x38, 0x7FFF_FFFE);
            bus.WriteLong(address + 0x3C, unchecked((uint)bootPri));
            bus.WriteLong(address + 0x40, DosTypeOFS);
        }

        private static void WriteFileSysStartupMsg(AmigaBus bus, uint address, int unit, uint deviceNameBstr, uint envec)
        {
            bus.WriteLong(address + 0x00, (uint)unit);
            bus.WriteLong(address + 0x04, deviceNameBstr >> 2);
            bus.WriteLong(address + 0x08, envec >> 2);
            bus.WriteLong(address + 0x0C, 0);
        }

        private static void WriteDeviceNode(AmigaBus bus, uint address, uint startup, uint dosName)
        {
            bus.ClearMemory(address, 0x30);
            bus.WriteLong(address + 0x04, 0);
            bus.WriteLong(address + 0x20, startup >> 2);
            bus.WriteLong(address + 0x28, 0xFFFF_FFFF);
            bus.WriteLong(address + 0x2C, (dosName + 4u) >> 2);
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
        {
            var length = checked((int)bus.ReadLong(ioRequestAddress + IoLengthOffset));
            var dataAddress = bus.ReadLong(ioRequestAddress + IoDataOffset);
            var offset = bus.ReadLong(ioRequestAddress + IoOffsetOffset);
            if (length < 0 || (length % AmigaHardfile.SectorSize) != 0 ||
                (offset % AmigaHardfile.SectorSize) != 0 ||
                !bus.IsMappedMemoryRange(dataAddress, length))
            {
                CompleteIo(bus, ioRequestAddress, IoErrBadLength, 0);
                return;
            }

            var buffer = new byte[length];
            hardfile.Read(offset, buffer);
            bus.CopyToMemory(dataAddress, buffer);
            CompleteIo(bus, ioRequestAddress, 0, (uint)length);
        }

        private static void ExecuteWrite(AmigaBus bus, uint ioRequestAddress, AmigaHardfile hardfile)
        {
            var length = checked((int)bus.ReadLong(ioRequestAddress + IoLengthOffset));
            var dataAddress = bus.ReadLong(ioRequestAddress + IoDataOffset);
            var offset = bus.ReadLong(ioRequestAddress + IoOffsetOffset);
            if (length < 0 || (length % AmigaHardfile.SectorSize) != 0 ||
                (offset % AmigaHardfile.SectorSize) != 0 ||
                !bus.IsMappedMemoryRange(dataAddress, length))
            {
                CompleteIo(bus, ioRequestAddress, IoErrBadLength, 0);
                return;
            }

            var buffer = new byte[length];
            bus.CopyFromMemory(dataAddress, buffer);
            hardfile.Write(offset, buffer);
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

        private byte ReadAutoConfigNibble(int offset)
        {
            return offset switch
            {
                0x00 => 0xD, // Zorro II, non-memory, link into expansion list, valid diagnostic ROM.
                0x02 => 0x1, // 64 KB board.
                0x04 => (byte)(ProductId >> 4),
                0x06 => (byte)(ProductId & 0x0F),
                0x10 => (byte)((ManufacturerId >> 12) & 0x0F),
                0x12 => (byte)((ManufacturerId >> 8) & 0x0F),
                0x14 => (byte)((ManufacturerId >> 4) & 0x0F),
                0x16 => (byte)(ManufacturerId & 0x0F),
                0x28 => 0x4, // Boot ROM vector high nibble.
                0x2A => 0x0,
                0x2C => 0x0,
                0x2E => 0x0,
                0x40 => 0x0,
                0x42 => 0x0,
                _ => 0x0
            };
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
