using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class CopperHdfControllerTests
{
	[Fact]
	public void HardfileReadsWritesAndDetectsRigidDiskBlock()
	{
		var path = CreateTempPath();
		try
		{
			AmigaHardfile.CreateBlank(path, 4 * AmigaHardfile.SectorSize);
			var rdb = new byte[AmigaHardfile.SectorSize];
			rdb[0] = (byte)'R';
			rdb[1] = (byte)'D';
			rdb[2] = (byte)'S';
			rdb[3] = (byte)'K';
			WriteUInt32(rdb, 0x1C, 2);
			var part = new byte[AmigaHardfile.SectorSize];
			part[0] = (byte)'P';
			part[1] = (byte)'A';
			part[2] = (byte)'R';
			part[3] = (byte)'T';
			WriteUInt32(part, 0x10, 0xFFFF_FFFF);
			File.WriteAllBytes(path, rdb.Concat(new byte[AmigaHardfile.SectorSize]).Concat(part).Concat(new byte[AmigaHardfile.SectorSize]).ToArray());

			using var hardfile = AmigaHardfile.Open(new AmigaHardfileConfiguration(0, path));
			var pattern = Enumerable.Range(0, AmigaHardfile.SectorSize).Select(value => (byte)value).ToArray();
			hardfile.Write(AmigaHardfile.SectorSize, pattern);
			var read = new byte[AmigaHardfile.SectorSize];
			hardfile.Read(AmigaHardfile.SectorSize, read);

			Assert.Equal(pattern, read);
			Assert.True(hardfile.HasRigidDiskBlock);
			Assert.Equal(new uint[] { 2 }, AmigaRigidDiskBlock.FindPartitionBlocks(hardfile));
		}
		finally
		{
			DeleteTemp(path);
		}
	}

	[Fact]
	public void AutoconfigMapsCopperHdfBoardIntoAssignedZorroIiBase()
	{
		var path = CreateTempPath();
		try
		{
			AmigaHardfile.CreateBlank(path, 2 * AmigaHardfile.SectorSize);
			using var machine = new AmigaMachine(AmigaMachineOptions
				.ForProfile(AmigaMachineProfile.A500Pal512KBoot)
				.WithHardfiles([new AmigaHardfileConfiguration(0, path)]));
			var bus = machine.Bus;

			Assert.Equal(0xD0, bus.ReadByte(CopperHdfController.AutoConfigBase));
			Assert.Equal(0xBF, bus.ReadByte(CopperHdfController.AutoConfigBase + 0x04));

			bus.WriteByte(CopperHdfController.AutoConfigBase + 0x48, 0xE0, 0);
			bus.WriteByte(CopperHdfController.AutoConfigBase + 0x4A, 0xA0, 0);

			Assert.True(bus.CopperHdf.IsConfigured);
			Assert.Equal(0x00EA_0000u, bus.CopperHdf.ConfiguredBase);
			Assert.Equal((byte)'c', bus.ReadByte(0x00EA_0100));
			Assert.Equal((byte)'o', bus.ReadByte(0x00EA_0101));
		}
		finally
		{
			DeleteTemp(path);
		}
	}

	[Fact]
	public void AutoconfigMapsKickstartIoSlotValueIntoZorroIiExpansionSpace()
	{
		var path = CreateTempPath();
		try
		{
			AmigaHardfile.CreateBlank(path, 2 * AmigaHardfile.SectorSize);
			using var machine = new AmigaMachine(AmigaMachineOptions
				.ForProfile(AmigaMachineProfile.A500Pal512KBoot)
				.WithHardfiles([new AmigaHardfileConfiguration(0, path)]));
			var bus = machine.Bus;

			bus.WriteByte(CopperHdfController.AutoConfigBase + 0x48, 0x00, 0);
			bus.WriteByte(CopperHdfController.AutoConfigBase + 0x4A, 0x90, 0);

			Assert.True(bus.CopperHdf.IsConfigured);
			Assert.Equal(0x00E9_0000u, bus.CopperHdf.ConfiguredBase);
			Assert.Equal(0x90, bus.ReadByte(0x00E9_4000));
		}
		finally
		{
			DeleteTemp(path);
		}
	}

	[Fact]
	public void BootstrapDiagnosticAreaSurvivesExpansionCopyToRam()
	{
		var path = CreateTempPath();
		try
		{
			AmigaHardfile.CreateBlank(path, 2 * AmigaHardfile.SectorSize);
			using var machine = new AmigaMachine(AmigaMachineOptions
				.ForProfile(AmigaMachineProfile.A500Pal512KBoot)
				.WithHardfiles([new AmigaHardfileConfiguration(0, path)]));
			var bus = machine.Bus;

			bus.WriteByte(CopperHdfController.AutoConfigBase + 0x48, 0xE0, 0);
			bus.WriteByte(CopperHdfController.AutoConfigBase + 0x4A, 0xA0, 0);

			Assert.True(bus.CopperHdf.BootstrapInstalled);
			var boardBase = bus.CopperHdf.ConfiguredBase;
			var diagBase = boardBase + CopperHdfController.DiagAreaOffset;
			Assert.Equal(0x90, bus.ReadByte(diagBase));
			Assert.Equal(CopperHdfController.DiagAreaCopySize, bus.ReadWord(diagBase + 0x02));
			Assert.Equal(CopperHdfController.DiagPointOffset, bus.ReadWord(diagBase + 0x04));
			Assert.Equal(CopperHdfController.BootPointOffset, bus.ReadWord(diagBase + 0x06));
			Assert.Equal(CopperHdfController.NameOffset, bus.ReadWord(diagBase + 0x08));
			Assert.Equal(0xFF00, bus.ReadWord(diagBase + CopperHdfController.DiagPointOffset));

			var copyBase = 0x3000u;
			for (var i = 0; i < CopperHdfController.DiagAreaCopySize; i++)
			{
				bus.WriteByte(copyBase + (uint)i, bus.ReadByte(diagBase + (uint)i), 0);
			}

			var state = new M68kCpuState();
			var execBase = 0x1000u;
			var configDev = 0x1800u;
			var expansionBase = 0x2000u;
			bus.WriteLong(4, execBase);
			bus.WriteByte(configDev + 0x0E, 0x02, 0);
			state.A[0] = boardBase;
			state.A[2] = copyBase;
			state.A[3] = configDev;
			state.A[5] = expansionBase;
			state.A[6] = execBase;

			var copiedDiagPoint = copyBase + CopperHdfController.DiagPointOffset;
			var diagTrapId = bus.ReadWord(copiedDiagPoint + 2);
			Assert.True(bus.HasHostTrapStub(copiedDiagPoint));
			Assert.True(bus.TryInvokeHostTrap(copiedDiagPoint, diagTrapId, state));

			var resident = copyBase + CopperHdfController.ResidentOffset;
			Assert.True(bus.CopperHdf.DiagBootstrapCalled);
			Assert.Equal(1u, state.D[0]);
			Assert.Equal(0x4AFC, bus.ReadWord(resident));
			Assert.Equal(resident, bus.ReadLong(resident + 0x02));
			Assert.Equal(copyBase + CopperHdfController.ResidentInitOffset + 4u, bus.ReadLong(resident + 0x06));
			Assert.Equal(copyBase + CopperHdfController.NameOffset, bus.ReadLong(resident + 0x0E));
			Assert.Equal(copyBase + CopperHdfController.IdStringOffset, bus.ReadLong(resident + 0x12));

			var copiedBootPoint = copyBase + CopperHdfController.BootPointOffset;
			var bootTrapId = bus.ReadWord(copiedBootPoint + 2);
			Assert.True(bus.HasHostTrapStub(copiedBootPoint));
			Assert.True(bus.TryInvokeHostTrap(copiedBootPoint, bootTrapId, state));
			Assert.True(bus.CopperHdf.BootBootstrapCalled);
			Assert.True(bus.CopperHdf.DeviceRegistered);
			Assert.True(bus.CopperHdf.BootNodeRegistered);

			var residentInit = bus.ReadLong(resident + 0x16);
			var initTrapId = bus.ReadWord(residentInit + 2);
			Assert.Equal(copyBase + CopperHdfController.ResidentInitOffset, residentInit);
			Assert.True(bus.HasHostTrapStub(residentInit));
			Assert.True(bus.TryInvokeHostTrap(residentInit, initTrapId, state));
			Assert.True(bus.CopperHdf.ResidentInitCalled);
			Assert.Equal(bus.CopperHdf.DeviceBase, bus.ReadLong(execBase + 0x015E));
			Assert.Equal(bus.CopperHdf.BootNodeAddress, bus.ReadLong(expansionBase + 0x004A));
			Assert.Equal(0, bus.ReadByte(configDev + 0x0E));
			Assert.Equal(bus.CopperHdf.DeviceBase, bus.ReadLong(configDev + 0x28));

			var bootNode = bus.CopperHdf.BootNodeAddress;
			var deviceNode = bus.CopperHdf.DeviceNodeAddress;
			Assert.Equal(16, bus.ReadByte(bootNode + 0x08));
			Assert.Equal(deviceNode, bus.ReadLong(bootNode + 0x10));
			Assert.Equal(0u, bus.ReadLong(deviceNode + 0x04));
			Assert.NotEqual(0u, bus.ReadLong(deviceNode + 0x20));
			Assert.Equal(0xFFFF_FFFFu, bus.ReadLong(deviceNode + 0x28));

			var ioAddress = 0x5000u;
			var openState = new M68kCpuState();
			openState.D[0] = 0;
			openState.A[1] = ioAddress;
			openState.A[6] = bus.CopperHdf.DeviceBase;
			var openTrap = bus.CopperHdf.DeviceBase - 6u;
			Assert.True(bus.HasHostTrapStub(openTrap));
			Assert.True(bus.TryInvokeHostTrap(openTrap, bus.ReadWord(openTrap + 2), openState));
			Assert.Equal(0u, openState.D[0]);
			Assert.Equal(bus.CopperHdf.DeviceBase, bus.ReadLong(ioAddress + 0x14));
			Assert.NotEqual(0u, bus.ReadLong(ioAddress + 0x18));

			bus.WriteWord(ioAddress + 0x1C, 15);
			var beginIoTrap = bus.CopperHdf.DeviceBase - 30u;
			openState.A[1] = ioAddress;
			Assert.True(bus.HasHostTrapStub(beginIoTrap));
			Assert.True(bus.TryInvokeHostTrap(beginIoTrap, bus.ReadWord(beginIoTrap + 2), openState));
			Assert.Equal(0, bus.ReadByte(ioAddress + 0x1F));
			Assert.Equal(0u, bus.ReadLong(ioAddress + 0x20));
		}
		finally
		{
			DeleteTemp(path);
		}
	}

	[Fact]
	public void DeviceIoRequestReadsAndWritesHardfileBlocks()
	{
		var path = CreateTempPath();
		try
		{
			AmigaHardfile.CreateBlank(path, 2 * AmigaHardfile.SectorSize);
			using var machine = new AmigaMachine(AmigaMachineOptions
				.ForProfile(AmigaMachineProfile.A500Pal512KBoot)
				.WithHardfiles([new AmigaHardfileConfiguration(0, path)]));
			var bus = machine.Bus;
			var unitAddress = 0x1000u;
			var ioAddress = 0x1100u;
			var dataAddress = 0x1200u;
			bus.WriteLong(unitAddress, 0);
			bus.WriteLong(ioAddress + 0x18, unitAddress);
			var source = Enumerable.Range(0, AmigaHardfile.SectorSize).Select(value => (byte)(255 - value)).ToArray();
			bus.CopyToMemory(dataAddress, source);

			bus.WriteWord(ioAddress + 0x1C, 3);
			bus.WriteLong(ioAddress + 0x24, AmigaHardfile.SectorSize);
			bus.WriteLong(ioAddress + 0x28, dataAddress);
			bus.WriteLong(ioAddress + 0x2C, AmigaHardfile.SectorSize);
			Assert.True(bus.CopperHdf.TryExecuteIoRequest(bus, ioAddress));
			Assert.Equal(0, bus.ReadByte(ioAddress + 0x1F));
			Assert.Equal((uint)AmigaHardfile.SectorSize, bus.ReadLong(ioAddress + 0x20));

			bus.ClearMemory(dataAddress, AmigaHardfile.SectorSize);
			bus.WriteWord(ioAddress + 0x1C, 2);
			Assert.True(bus.CopperHdf.TryExecuteIoRequest(bus, ioAddress));

			var destination = new byte[AmigaHardfile.SectorSize];
			bus.CopyFromMemory(dataAddress, destination);
			Assert.Equal(source, destination);
		}
		finally
		{
			DeleteTemp(path);
		}
	}

	private static string CreateTempPath()
		=> Path.Combine(Path.GetTempPath(), "copperhdf-" + Guid.NewGuid().ToString("N") + ".hdf");

	private static void DeleteTemp(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch (IOException)
		{
		}
	}

	private static void WriteUInt32(byte[] data, int offset, uint value)
	{
		data[offset] = (byte)(value >> 24);
		data[offset + 1] = (byte)(value >> 16);
		data[offset + 2] = (byte)(value >> 8);
		data[offset + 3] = (byte)value;
	}
}
