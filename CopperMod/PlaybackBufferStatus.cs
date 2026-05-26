namespace CopperMod;

internal readonly struct PlaybackBufferStatus
{
	public PlaybackBufferStatus(
		TimeSpan queuedDuration,
		TimeSpan targetDuration,
		TimeSpan capacityDuration,
		long underrunCount,
		bool producerEnded,
		bool endOfSong)
	{
		QueuedDuration = queuedDuration;
		TargetDuration = targetDuration;
		CapacityDuration = capacityDuration;
		UnderrunCount = underrunCount;
		ProducerEnded = producerEnded;
		EndOfSong = endOfSong;
	}

	public TimeSpan QueuedDuration { get; }

	public TimeSpan TargetDuration { get; }

	public TimeSpan CapacityDuration { get; }

	public long UnderrunCount { get; }

	public bool ProducerEnded { get; }

	public bool EndOfSong { get; }
}
