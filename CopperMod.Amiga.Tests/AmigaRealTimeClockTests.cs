namespace CopperMod.Amiga.Tests;

public sealed class AmigaRealTimeClockTests
{
	[Fact]
	public void RealTimeClockMapsBcdDateTimeNibblesAtA501Address()
	{
		var now = new DateTimeOffset(2026, 6, 14, 21, 45, 37, TimeSpan.Zero);
		var bus = new AmigaBus(
			realTimeClockEnabled: true,
			realTimeClockNowProvider: () => now);

		Assert.True(bus.RealTimeClockEnabled);
		Assert.Equal(7, ReadRtcRegister(bus, 0x0));
		Assert.Equal(3, ReadRtcRegister(bus, 0x1));
		Assert.Equal(5, ReadRtcRegister(bus, 0x2));
		Assert.Equal(4, ReadRtcRegister(bus, 0x3));
		Assert.Equal(1, ReadRtcRegister(bus, 0x4));
		Assert.Equal(2, ReadRtcRegister(bus, 0x5));
		Assert.Equal(4, ReadRtcRegister(bus, 0x6));
		Assert.Equal(1, ReadRtcRegister(bus, 0x7));
		Assert.Equal(6, ReadRtcRegister(bus, 0x8));
		Assert.Equal(0, ReadRtcRegister(bus, 0x9));
		Assert.Equal(6, ReadRtcRegister(bus, 0xA));
		Assert.Equal(2, ReadRtcRegister(bus, 0xB));
		Assert.Equal(0, ReadRtcRegister(bus, 0xC));
		Assert.Equal(4, ReadRtcRegister(bus, 0xF));
	}

	[Fact]
	public void RealTimeClockWritesAdjustSessionClockOffset()
	{
		var now = new DateTimeOffset(2026, 6, 14, 21, 45, 37, TimeSpan.Zero);
		var bus = new AmigaBus(
			realTimeClockEnabled: true,
			realTimeClockNowProvider: () => now);
		var cycle = 0L;

		bus.WriteByte(AmigaRealTimeClock.BaseAddress + 0x0 * 4, 9, ref cycle, AmigaBusAccessKind.CpuDataWrite);
		bus.WriteByte(AmigaRealTimeClock.BaseAddress + 0x2 * 4, 8, ref cycle, AmigaBusAccessKind.CpuDataWrite);

		Assert.Equal(9, ReadRtcRegister(bus, 0x0));
		Assert.Equal(3, ReadRtcRegister(bus, 0x1));
		Assert.Equal(8, ReadRtcRegister(bus, 0x2));
		Assert.Equal(4, ReadRtcRegister(bus, 0x3));
	}

	[Theory]
	[InlineData(2, 6, 4)]
	[InlineData(7, 8, 0)]
	public void RealTimeClockExpandsWrittenYearUsingAmigaBattClockWindow(byte tens, byte ones, byte expectedWeekday)
	{
		var now = new DateTimeOffset(1978, 1, 1, 0, 0, 0, TimeSpan.Zero);
		var bus = new AmigaBus(
			realTimeClockEnabled: true,
			realTimeClockNowProvider: () => now);
		var cycle = 0L;

		bus.WriteByte(AmigaRealTimeClock.BaseAddress + 0xB * 4, tens, ref cycle, AmigaBusAccessKind.CpuDataWrite);
		bus.WriteByte(AmigaRealTimeClock.BaseAddress + 0xA * 4, ones, ref cycle, AmigaBusAccessKind.CpuDataWrite);

		Assert.Equal(ones, ReadRtcRegister(bus, 0xA));
		Assert.Equal(tens, ReadRtcRegister(bus, 0xB));
		Assert.Equal(expectedWeekday, ReadRtcRegister(bus, 0xC));
	}

	[Fact]
	public void DisabledRealTimeClockLeavesA501AddressUnmapped()
	{
		var bus = new AmigaBus();
		var cycle = 0L;

		Assert.False(bus.RealTimeClockEnabled);
		Assert.Equal(0, bus.ReadByte(AmigaRealTimeClock.BaseAddress, ref cycle, AmigaBusAccessKind.CpuDataRead));
		Assert.DoesNotContain(bus.BusAccesses, access => access.Request.Target == AmigaBusAccessTarget.RealTimeClock);
	}

	private static byte ReadRtcRegister(AmigaBus bus, int register)
	{
		var cycle = 0L;
		return bus.ReadByte(AmigaRealTimeClock.BaseAddress + (uint)(register * 4), ref cycle, AmigaBusAccessKind.CpuDataRead);
	}
}
