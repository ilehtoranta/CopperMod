using System;
using CopperMod.Sid;

namespace CopperMod.Sid.Tests;

public sealed class SidResamplerTests
{
	private const int PalClock = SidConstants.PalCpuCyclesPerSecond;

	[Theory]
	[InlineData(44100)]
	[InlineData(48000)]
	public void DcGainIsUnity(int sampleRate)
	{
		var resampler = new SidWindowedSincResampler(PalClock, sampleRate);

		var coefficientSum = 0.0;
		foreach (var c in resampler.Coefficients)
		{
			coefficientSum += c;
		}

		Assert.Equal(1.0, coefficientSum, 9);

		// Prime the delay line with a DC input and confirm the output converges to it.
		for (var i = 0; i < resampler.Taps; i++)
		{
			resampler.Push(1.0);
		}

		Assert.Equal(1.0, resampler.Read(), 9);
	}

	[Theory]
	[InlineData(44100)]
	[InlineData(48000)]
	public void ImpulseResponseIsSymmetric(int sampleRate)
	{
		var resampler = new SidWindowedSincResampler(PalClock, sampleRate);
		var coeff = resampler.Coefficients;

		Assert.True((coeff.Length & 1) == 1, "Kaiser FIR length should be odd for an integer center tap.");
		for (var j = 0; j < coeff.Length / 2; j++)
		{
			Assert.True(
				Math.Abs(coeff[j] - coeff[coeff.Length - 1 - j]) < 1e-12,
				$"Coefficient {j} is not symmetric with {coeff.Length - 1 - j}.");
		}
	}

	[Theory]
	[InlineData(44100)]
	[InlineData(48000)]
	public void PassbandIsFlatThrough20kHz(int sampleRate)
	{
		var resampler = new SidWindowedSincResampler(PalClock, sampleRate);

		foreach (var frequency in new[] { 1_000.0, 5_000.0, 10_000.0, 15_000.0, 19_000.0 })
		{
			var db = MagnitudeDb(resampler, frequency);
			Assert.True(Math.Abs(db) <= 0.1, $"Passband gain at {frequency} Hz was {db:0.###} dB (expected flat within 0.1 dB).");
		}

		// 20 kHz sits inside the passband for fs >= ~43.5 kHz (passband edge = 0.92 * fs/2).
		var db20k = MagnitudeDb(resampler, 20_000.0);
		Assert.True(Math.Abs(db20k) <= 0.5, $"Gain at 20 kHz was {db20k:0.###} dB (expected within 0.5 dB; boxcar drooped ~-3.9 dB).");
	}

	[Theory]
	[InlineData(44100)]
	[InlineData(48000)]
	public void StopbandRejectionAtLeast90dB(int sampleRate)
	{
		var resampler = new SidWindowedSincResampler(PalClock, sampleRate);

		var nyquist = sampleRate * 0.5;
		var top = PalClock * 0.5;
		const int points = 200;
		var worst = double.NegativeInfinity;
		var worstFrequency = 0.0;
		for (var i = 0; i <= points; i++)
		{
			var frequency = nyquist + ((top - nyquist) * i / points);
			var db = MagnitudeDb(resampler, frequency);
			if (db > worst)
			{
				worst = db;
				worstFrequency = frequency;
			}
		}

		Assert.True(worst <= -90.0, $"Worst stopband/alias rejection was {worst:0.##} dB at {worstFrequency:0} Hz (expected <= -90 dB; boxcar ~ -13 dB).");
	}

	[Fact]
	public void TapCountIsOddInRangeAndScalesWithTransitionWidth()
	{
		var wide = new SidWindowedSincResampler(PalClock, 48000);
		var narrow = new SidWindowedSincResampler(PalClock, 44100);

		Assert.True((wide.Taps & 1) == 1 && (narrow.Taps & 1) == 1, "FIR lengths should be odd.");
		Assert.InRange(wide.Taps, 15, 16383);
		Assert.InRange(narrow.Taps, 15, 16383);

		// Lower sample rate => narrower transition band (in Hz) => more taps.
		Assert.True(narrow.Taps > wide.Taps, $"Expected more taps at 44.1 kHz ({narrow.Taps}) than 48 kHz ({wide.Taps}).");
		Assert.Equal(narrow.GroupDelayCycles, (narrow.Taps - 1) / 2);
	}

	[Fact]
	public void SubstantiallyOutperformsBoxcarAt20kHzAndAlias()
	{
		var resampler = new SidWindowedSincResampler(PalClock, 44100);

		// The old decimator was a moving average of N = round(fclk / fs) input cycles.
		var boxcarLength = (int)Math.Round((double)PalClock / 44100);

		var firAt20k = MagnitudeDb(resampler, 20_000.0);
		var boxcarAt20k = BoxcarMagnitudeDb(boxcarLength, 20_000.0);
		Assert.True(firAt20k > boxcarAt20k + 3.0, $"FIR 20 kHz gain {firAt20k:0.##} dB should beat boxcar {boxcarAt20k:0.##} dB.");

		// Worst-case alias rejection above Nyquist.
		var firAlias = WorstStopband(resampler, 44100);
		var boxcarAlias = WorstBoxcarStopband(boxcarLength, 44100);
		Assert.True(firAlias < boxcarAlias - 50.0, $"FIR alias rejection {firAlias:0.##} dB should far exceed boxcar {boxcarAlias:0.##} dB.");
	}

	private static double MagnitudeDb(SidWindowedSincResampler resampler, double frequency)
	{
		var omega = 2.0 * Math.PI * frequency / PalClock;
		var coeff = resampler.Coefficients;
		var re = 0.0;
		var im = 0.0;
		for (var j = 0; j < coeff.Length; j++)
		{
			re += coeff[j] * Math.Cos(omega * j);
			im -= coeff[j] * Math.Sin(omega * j);
		}

		var magnitude = Math.Sqrt((re * re) + (im * im));
		return 20.0 * Math.Log10(Math.Max(magnitude, 1e-12));
	}

	private static double WorstStopband(SidWindowedSincResampler resampler, int sampleRate)
	{
		var nyquist = sampleRate * 0.5;
		var top = PalClock * 0.5;
		var worst = double.NegativeInfinity;
		for (var i = 0; i <= 200; i++)
		{
			var frequency = nyquist + ((top - nyquist) * i / 200);
			worst = Math.Max(worst, MagnitudeDb(resampler, frequency));
		}

		return worst;
	}

	// Frequency response of an N-point moving average (the former boxcar decimator),
	// evaluated on the same input clock grid.
	private static double BoxcarMagnitudeDb(int length, double frequency)
	{
		var omega = 2.0 * Math.PI * frequency / PalClock;
		var re = 0.0;
		var im = 0.0;
		for (var j = 0; j < length; j++)
		{
			re += Math.Cos(omega * j);
			im -= Math.Sin(omega * j);
		}

		var magnitude = Math.Sqrt((re * re) + (im * im)) / length;
		return 20.0 * Math.Log10(Math.Max(magnitude, 1e-12));
	}

	private static double WorstBoxcarStopband(int length, int sampleRate)
	{
		var nyquist = sampleRate * 0.5;
		var top = PalClock * 0.5;
		var worst = double.NegativeInfinity;
		for (var i = 0; i <= 200; i++)
		{
			var frequency = nyquist + ((top - nyquist) * i / 200);
			worst = Math.Max(worst, BoxcarMagnitudeDb(length, frequency));
		}

		return worst;
	}
}
