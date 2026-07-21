using System.Diagnostics;

namespace CopperScreen;

/// <summary>Host-only CRT phosphor persistence for completed interlaced fields.</summary>
internal sealed class CrtPhosphorComposer
{
	// Short-persistence consumer CRT phosphor: the visible contribution halves every 24 ms.
	private const double PhosphorHalfLifeSeconds = 0.024;
	private int _width;
	private int _height;
	private float[]? _red;
	private float[]? _green;
	private float[]? _blue;
	private int[]? _output;
	private long _lastUpdateTimestamp;
	private bool _initialized;

	public bool HasBuffers => _output != null;

	public int[] Output => _output ?? Array.Empty<int>();

	public void SubmitField(ReadOnlySpan<int> fieldHistory, int width, int height, int interlaceField, double fieldDurationSeconds, long timestamp)
	{
		EnsureBuffers(width, height);
		Advance(timestamp);

		var rowDuration = fieldDurationSeconds / Math.Max(1, height >> 1);
		for (var y = 0; y < height; y++)
		{
			if (!_initialized && (y & 1) != interlaceField)
			{
				InjectRow(fieldHistory, y, 0.5 * DecayForSeconds(rowDuration * (height - 1 - y)));
			}
			else if ((y & 1) == interlaceField)
			{
				InjectRow(fieldHistory, y, DecayForSeconds(rowDuration * (height - 1 - y)));
			}
		}

		_initialized = true;
		PackOutput();
	}

	public void Advance(long timestamp)
	{
		if (!HasBuffers)
		{
			return;
		}

		if (_lastUpdateTimestamp != 0)
		{
			var elapsed = Stopwatch.GetElapsedTime(_lastUpdateTimestamp, timestamp).TotalSeconds;
			var decay = DecayForSeconds(Math.Max(0, elapsed));
			for (var i = 0; i < _output!.Length; i++)
			{
				_red![i] *= (float)decay;
				_green![i] *= (float)decay;
				_blue![i] *= (float)decay;
			}
		}

		_lastUpdateTimestamp = timestamp;
		PackOutput();
	}

	public void Reset()
	{
		_width = 0;
		_height = 0;
		_red = null;
		_green = null;
		_blue = null;
		_output = null;
		_lastUpdateTimestamp = 0;
		_initialized = false;
	}

	internal static double DecayForSeconds(double seconds)
		=> Math.Pow(0.5, seconds / PhosphorHalfLifeSeconds);

	private void EnsureBuffers(int width, int height)
	{
		if (width == _width && height == _height && HasBuffers)
		{
			return;
		}

		_width = width;
		_height = height;
		var length = checked(width * height);
		_red = new float[length];
		_green = new float[length];
		_blue = new float[length];
		_output = new int[length];
		_lastUpdateTimestamp = 0;
		_initialized = false;
	}

	private void InjectRow(ReadOnlySpan<int> fieldHistory, int y, double intensity)
	{
		var offset = y * _width;
		for (var x = 0; x < _width; x++)
		{
			var value = unchecked((uint)fieldHistory[offset + x]);
			_red![offset + x] = Math.Min(255, _red[offset + x] + (float)(((value >> 16) & 0xFF) * intensity));
			_green![offset + x] = Math.Min(255, _green[offset + x] + (float)(((value >> 8) & 0xFF) * intensity));
			_blue![offset + x] = Math.Min(255, _blue[offset + x] + (float)((value & 0xFF) * intensity));
		}
	}

	private void PackOutput()
	{
		if (!HasBuffers)
		{
			return;
		}

		for (var i = 0; i < _output!.Length; i++)
		{
			_output[i] = unchecked((int)(0xFF00_0000u |
				((uint)Math.Clamp((int)_red![i], 0, 255) << 16) |
				((uint)Math.Clamp((int)_green![i], 0, 255) << 8) |
				(uint)Math.Clamp((int)_blue![i], 0, 255)));
		}
	}
}
