using System.Buffers.Binary;

namespace CopperMod.Tools;

internal sealed class FloatPcmWriter : IDisposable
{
	private readonly Stream _stream;
	private byte[] _buffer = Array.Empty<byte>();

	public FloatPcmWriter(Stream stream)
	{
		_stream = stream ?? throw new ArgumentNullException(nameof(stream));
	}

	public void Write(float[] samples, int count)
	{
		if (count < 0 || count > samples.Length)
		{
			throw new ArgumentOutOfRangeException(nameof(count));
		}

		EnsureCapacity(checked(count * sizeof(float)));
		var bytes = _buffer.AsSpan(0, count * sizeof(float));
		for (var i = 0; i < count; i++)
		{
			BinaryPrimitives.WriteSingleLittleEndian(bytes.Slice(i * sizeof(float), sizeof(float)), samples[i]);
		}

		_stream.Write(bytes);
	}

	public void Dispose()
	{
		_stream.Dispose();
	}

	private void EnsureCapacity(int byteCount)
	{
		if (_buffer.Length < byteCount)
		{
			_buffer = new byte[byteCount];
		}
	}
}
