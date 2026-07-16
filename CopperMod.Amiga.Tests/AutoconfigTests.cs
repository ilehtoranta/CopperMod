using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class AutoconfigTests
{
	[Theory]
	[InlineData(64 * 1024, 0xE1)]
	[InlineData(128 * 1024, 0xE2)]
	[InlineData(256 * 1024, 0xE3)]
	[InlineData(512 * 1024, 0xE4)]
	[InlineData(1024 * 1024, 0xE5)]
	[InlineData(2 * 1024 * 1024, 0xE6)]
	[InlineData(4 * 1024 * 1024, 0xE7)]
	[InlineData(8 * 1024 * 1024, 0xE0)]
	public void FastRamIdentityEncodesZorroIiSizes(int size, byte expectedType)
	{
		var identity = AutoconfigFastRamBoard.CreateIdentity(size);

		Assert.Equal(AutoconfigBusKind.ZorroII, identity.BusKind);
		Assert.Equal(expectedType, identity.Type);
		Assert.Equal(0x80, identity.Flags);
		Assert.Equal(0x07DB, identity.ManufacturerId);
		Assert.Equal(0x49, identity.ProductId);
	}

	[Theory]
	[InlineData(16 * 1024 * 1024, 0xA0)]
	[InlineData(32 * 1024 * 1024, 0xA1)]
	[InlineData(64 * 1024 * 1024, 0xA2)]
	[InlineData(128 * 1024 * 1024, 0xA3)]
	[InlineData(256 * 1024 * 1024, 0xA4)]
	[InlineData(512 * 1024 * 1024, 0xA5)]
	[InlineData(1024 * 1024 * 1024, 0xA6)]
	public void FastRamIdentityEncodesExtendedZorroIiiSizes(int size, byte expectedType)
	{
		var identity = AutoconfigFastRamBoard.CreateIdentity(size);

		Assert.Equal(AutoconfigBusKind.ZorroIII, identity.BusKind);
		Assert.Equal(expectedType, identity.Type);
		Assert.Equal(0xB0, identity.Flags);
	}

	[Fact]
	public void ZorroIiIdentityUsesPairedComplementedNibbles()
	{
		var board = new TestBoard(AutoconfigIdentity.CreateFastRam(8 * 1024 * 1024));
		var chain = new AutoconfigChain([board]);

		Assert.True(chain.TryReadByte(AutoconfigChain.ZorroIIConfigBase, out var typeHigh));
		Assert.True(chain.TryReadByte(AutoconfigChain.ZorroIIConfigBase + 2, out var typeLow));
		Assert.True(chain.TryReadByte(AutoconfigChain.ZorroIIConfigBase + 4, out var productHigh));
		Assert.Equal(0xE0, typeHigh);
		Assert.Equal(0x00, typeLow);
		Assert.Equal(0xBF, productHigh);
	}

	[Fact]
	public void ZorroIiiIdentityUsesHundredByteNibbleStride()
	{
		var board = new TestBoard(AutoconfigIdentity.CreateFastRam(16 * 1024 * 1024));
		var chain = new AutoconfigChain([board]);

		Assert.True(chain.TryReadByte(AutoconfigChain.ZorroIIIConfigBase, out var typeHigh));
		Assert.True(chain.TryReadByte(AutoconfigChain.ZorroIIIConfigBase + 0x100, out var typeLow));
		Assert.True(chain.TryReadByte(AutoconfigChain.ZorroIIIConfigBase + 4, out var productHigh));
		Assert.Equal(0xA0, typeHigh);
		Assert.Equal(0x00, typeLow);
		Assert.Equal(0xBF, productHigh);
	}

	[Fact]
	public void ZorroIiAssignmentAcceptsDocumentedAndLegacyWriteOrders()
	{
		var documented = new TestBoard(AutoconfigIdentity.CreateFastRam(8 * 1024 * 1024));
		var documentedChain = new AutoconfigChain([documented]);
		documentedChain.TryWriteByte(AutoconfigChain.ZorroIIConfigBase + 0x4A, 0x00);
		documentedChain.TryWriteByte(AutoconfigChain.ZorroIIConfigBase + 0x48, 0x20);

		var legacy = new TestBoard(AutoconfigIdentity.CreateIoBoard(64 * 1024, 0x07DB, 0x48, 0x4000));
		var legacyChain = new AutoconfigChain([legacy]);
		legacyChain.TryWriteByte(AutoconfigChain.ZorroIIConfigBase + 0x48, 0xE0);
		legacyChain.TryWriteByte(AutoconfigChain.ZorroIIConfigBase + 0x4A, 0xA0);

		Assert.Equal(0x0020_0000u, documented.ConfiguredBase);
		Assert.Equal(0x00EA_0000u, legacy.ConfiguredBase);
	}

	[Fact]
	public void ZorroIiiAssignmentAcceptsByteAndWordBaseWrites()
	{
		var byteBoard = new TestBoard(AutoconfigIdentity.CreateFastRam(16 * 1024 * 1024));
		var byteChain = new AutoconfigChain([byteBoard]);
		byteChain.TryWriteByte(AutoconfigChain.ZorroIIIConfigBase + 0x48, 0x00);
		byteChain.TryWriteByte(AutoconfigChain.ZorroIIIConfigBase + 0x44, 0x10);

		var wordBoard = new TestBoard(AutoconfigIdentity.CreateFastRam(32 * 1024 * 1024));
		var wordChain = new AutoconfigChain([wordBoard]);
		wordChain.TryWriteWord(AutoconfigChain.ZorroIIIConfigBase + 0x44, 0x2000);

		Assert.Equal(0x1000_0000u, byteBoard.ConfiguredBase);
		Assert.Equal(0x2000_0000u, wordBoard.ConfiguredBase);
	}

	[Fact]
	public void MixedChainAdvancesBetweenConfigurationAperturesAndResets()
	{
		var zorroIII = new TestBoard(AutoconfigIdentity.CreateFastRam(16 * 1024 * 1024));
		var zorroII = new TestBoard(AutoconfigIdentity.CreateIoBoard(64 * 1024, 0x07DB, 0x48, 0x4000));
		var chain = new AutoconfigChain([zorroIII, zorroII]);

		Assert.False(chain.TryReadByte(AutoconfigChain.ZorroIIConfigBase, out _));
		Assert.True(chain.TryReadByte(AutoconfigChain.ZorroIIIConfigBase, out _));
		chain.TryWriteWord(AutoconfigChain.ZorroIIIConfigBase + 0x44, 0x1000);
		Assert.True(chain.TryReadByte(AutoconfigChain.ZorroIIConfigBase, out _));
		chain.TryWriteByte(AutoconfigChain.ZorroIIConfigBase + 0x4C, 0);
		Assert.False(chain.TryReadByte(AutoconfigChain.ZorroIIConfigBase, out _));

		chain.ResetConfiguration();

		Assert.False(zorroIII.IsConfigured);
		Assert.False(zorroII.IsShutUp);
		Assert.True(chain.TryReadByte(AutoconfigChain.ZorroIIIConfigBase, out _));
	}

	private sealed class TestBoard : AutoconfigBoard
	{
		public TestBoard(AutoconfigIdentity identity)
			: base(identity)
		{
		}

		public override bool ContainsBoardAddress(uint address)
			=> IsConfigured && address - ConfiguredBase < Identity.Size;

		public override byte ReadBoardByte(uint address)
			=> ContainsBoardAddress(address) ? (byte)0 : throw new ArgumentOutOfRangeException(nameof(address));

		public override bool TryWriteBoardByte(uint address, byte value)
			=> ContainsBoardAddress(address);
	}
}
