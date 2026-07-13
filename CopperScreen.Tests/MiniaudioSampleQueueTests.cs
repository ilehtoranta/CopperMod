using CopperScreen;

namespace CopperScreen.Tests;

public sealed class MiniaudioSampleQueueTests
{
	[Fact]
	public void DequeuePreservesSamplesAcrossCallbackBoundaries()
	{
		var queue = new MiniaudioSampleQueue(samplesPerBuffer: 4, bufferCount: 3);
		Assert.True(queue.TryEnqueue([1f, 2f, 3f, 4f]));
		Assert.True(queue.TryEnqueue([5f, 6f, 7f, 8f]));

		var first = new float[3];
		queue.Dequeue(first);
		var second = new float[5];
		queue.Dequeue(second);

		Assert.Equal([1f, 2f, 3f], first);
		Assert.Equal([4f, 5f, 6f, 7f, 8f], second);
		Assert.Equal(0, queue.QueuedBufferCount);
	}

	[Fact]
	public void DequeueWritesSilenceAfterQueuedAudioRunsOut()
	{
		var queue = new MiniaudioSampleQueue(samplesPerBuffer: 4, bufferCount: 2);
		Assert.True(queue.TryEnqueue([0.25f, -0.25f]));
		var output = new float[4];

		queue.Dequeue(output);

		Assert.Equal([0.25f, -0.25f, 0f, 0f], output);
	}

	[Fact]
	public void FullQueueRejectsAdditionalBuffersAndRecoversAfterRead()
	{
		var queue = new MiniaudioSampleQueue(samplesPerBuffer: 2, bufferCount: 2);
		Assert.True(queue.TryEnqueue([1f, 2f]));
		Assert.True(queue.TryEnqueue([3f, 4f]));
		Assert.False(queue.TryEnqueue([5f, 6f]));

		queue.Dequeue(new float[2]);

		Assert.True(queue.TryEnqueue([5f, 6f]));
		Assert.Equal(2, queue.QueuedBufferCount);
	}
}
