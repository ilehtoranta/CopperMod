using System;

namespace CopperMod.Sid
{
    /// <summary>
    /// Kaiser windowed-sinc FIR decimator that band-limits the full-rate SID output
    /// (synthesized once per CPU cycle, ~985 kHz PAL) before it is sampled at the host
    /// output rate.
    ///
    /// It replaces the former rectangular-window (boxcar) average, whose sinc(f/fs)
    /// response drooped the treble (~-3.9 dB at 20 kHz) and rejected aliases by only
    /// ~13 dB worst case. This filter targets a flat passband out to
    /// PassbandNyquistFraction of the output Nyquist frequency and a stopband/alias
    /// rejection of StopbandAttenuationDb dB.
    ///
    /// The kernel is symmetric (linear phase). It is applied causally over the most
    /// recent input samples, so the output carries a constant group delay of
    /// (taps - 1) / 2 input cycles - the standard windowed-sinc resampler structure.
    /// Filter design allocates and therefore lives on a cold path (Configure); Push
    /// and Read are allocation-free hot-path operations over preallocated buffers.
    /// </summary>
    internal sealed class SidWindowedSincResampler
    {
        // Kaiser design targets. Passband edge and stopband edge are expressed as
        // fractions of the output Nyquist frequency (fs / 2). Keeping the passband
        // edge above 20 kHz for fs >= 44.1 kHz is what removes the audible treble
        // droop of the old boxcar average.
        private const double StopbandAttenuationDb = 96.0;
        private const double PassbandNyquistFraction = 0.92;
        private const int MinTaps = 15;
        private const int MaxTaps = 16383;

        private readonly int _clockRate;
        private double[] _coefficients = Array.Empty<double>();
        private double[] _ring = Array.Empty<double>();
        private int _ringMask;
        private int _head;
        private int _taps;
        private int _sampleRate;

        public SidWindowedSincResampler(int clockRate, int sampleRate)
        {
            _clockRate = clockRate > 0 ? clockRate : SidConstants.PalCpuCyclesPerSecond;
            Configure(sampleRate);
        }

        /// <summary>Number of FIR taps in the current design.</summary>
        public int Taps => _taps;

        /// <summary>Configured output sample rate the filter is currently designed for.</summary>
        public int SampleRate => _sampleRate;

        /// <summary>Constant group delay of the linear-phase kernel, in input cycles.</summary>
        public int GroupDelayCycles => (_taps - 1) / 2;

        /// <summary>The normalized FIR coefficients (DC gain sums to 1).</summary>
        public ReadOnlySpan<double> Coefficients => _coefficients;

        /// <summary>
        /// (Re)designs the FIR for the given output sample rate. No-op when the rate
        /// is unchanged, so it is safe to call once per render tick from a cold path.
        /// </summary>
        [HotPathAllocationAllowed("Filter (re)design is a cold configuration step, not a per-sample operation.")]
        public void Configure(int sampleRate)
        {
            if (sampleRate <= 0 || sampleRate == _sampleRate)
            {
                return;
            }

            _sampleRate = sampleRate;
            DesignFilter();
        }

        /// <summary>Clears the input history so playback restarts from silence.</summary>
        public void Reset()
        {
            if (_ring.Length > 0)
            {
                Array.Clear(_ring);
            }

            _head = 0;
        }

        /// <summary>Feeds one full-rate input sample (one CPU cycle) into the delay line.</summary>
        [HotPath]
        public void Push(double sample)
        {
            _ring[_head] = sample;
            _head = (_head + 1) & _ringMask;
        }

        /// <summary>
        /// Convolves the FIR against the most recent input samples and returns the
        /// band-limited value (delayed by GroupDelayCycles cycles).
        /// </summary>
        [HotPath]
        public double Read()
        {
            var coeff = _coefficients;
            var ring = _ring;
            var mask = _ringMask;
            var index = (_head - 1) & mask;
            var sum = 0.0;
            for (var j = 0; j < coeff.Length; j++)
            {
                sum += coeff[j] * ring[index];
                index = (index - 1) & mask;
            }

            return sum;
        }

        private void DesignFilter()
        {
            var nyquist = _sampleRate * 0.5;
            var passEdge = PassbandNyquistFraction * nyquist; // Hz
            var stopEdge = nyquist;                           // Hz: no signal energy survives above Nyquist
            var cutoff = 0.5 * (passEdge + stopEdge);         // -6 dB point, Hz
            var transition = stopEdge - passEdge;             // Hz

            // Kaiser filter length from the transition width (expressed in rad/sample at
            // the input clock rate) and the desired stopband attenuation.
            var transitionOmega = 2.0 * Math.PI * transition / _clockRate;
            var taps = (int)Math.Ceiling((StopbandAttenuationDb - 8.0) / (2.285 * transitionOmega));
            if (taps < MinTaps)
            {
                taps = MinTaps;
            }

            if (taps > MaxTaps)
            {
                taps = MaxTaps;
            }

            if ((taps & 1) == 0)
            {
                taps++; // odd length -> integer center tap, exact linear phase
            }

            var beta = KaiserBeta(StopbandAttenuationDb);
            var center = (taps - 1) / 2;
            var omegaC = 2.0 * Math.PI * cutoff / _clockRate;
            var i0Beta = BesselI0(beta);

            var coeff = _coefficients.Length == taps ? _coefficients : new double[taps];
            var sum = 0.0;
            for (var n = 0; n < taps; n++)
            {
                var x = n - center;
                var sinc = x == 0 ? omegaC / Math.PI : Math.Sin(omegaC * x) / (Math.PI * x);
                var r = (double)x / center;
                var window = BesselI0(beta * Math.Sqrt(1.0 - (r * r))) / i0Beta;
                var tap = sinc * window;
                coeff[n] = tap;
                sum += tap;
            }

            // Normalize to unity DC gain.
            var inv = 1.0 / sum;
            for (var n = 0; n < taps; n++)
            {
                coeff[n] *= inv;
            }

            _coefficients = coeff;
            _taps = taps;

            var ringSize = 1;
            while (ringSize < taps)
            {
                ringSize <<= 1;
            }

            if (_ring.Length != ringSize)
            {
                _ring = new double[ringSize];
            }
            else
            {
                Array.Clear(_ring);
            }

            _ringMask = ringSize - 1;
            _head = 0;
        }

        // Kaiser window shape parameter for a target stopband attenuation (dB).
        private static double KaiserBeta(double attenuationDb)
        {
            if (attenuationDb > 50.0)
            {
                return 0.1102 * (attenuationDb - 8.7);
            }

            if (attenuationDb >= 21.0)
            {
                return (0.5842 * Math.Pow(attenuationDb - 21.0, 0.4)) + (0.07886 * (attenuationDb - 21.0));
            }

            return 0.0;
        }

        // Modified Bessel function of the first kind, order 0, via its power series.
        private static double BesselI0(double x)
        {
            var sum = 1.0;
            var term = 1.0;
            var xx = x * x;
            for (var k = 1; k < 64; k++)
            {
                term *= xx / (4.0 * k * k);
                sum += term;
                if (term < 1e-18 * sum)
                {
                    break;
                }
            }

            return sum;
        }
    }
}
