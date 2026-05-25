namespace CopperMod;

internal static class WaveformSmoother
{
	public static WaveformSnapshot MoveTowards(
		WaveformSnapshot? current,
		WaveformSnapshot target,
		float amount,
		out bool settled)
	{
		if (target is null)
		{
			throw new ArgumentNullException(nameof(target));
		}

		if (amount <= 0.0f || amount > 1.0f)
		{
			throw new ArgumentOutOfRangeException(nameof(amount));
		}

		if (amount >= 1.0f)
		{
			settled = true;
			return target;
		}

		if (current == null ||
			current.ChannelCount != target.ChannelCount ||
			current.BinCount != target.BinCount ||
			current.BinCount == 0)
		{
			settled = true;
			return target;
		}

		const float settleThreshold = 0.002f;
		var channels = new WaveformChannelSnapshot[target.ChannelCount];
		var maxDelta = 0.0f;
		for (var channelIndex = 0; channelIndex < target.ChannelCount; channelIndex++)
		{
			var currentChannel = current.Channels[channelIndex];
			var targetChannel = target.Channels[channelIndex];
			var minimums = new float[target.BinCount];
			var maximums = new float[target.BinCount];
			var values = new float[target.BinCount];
			for (var i = 0; i < target.BinCount; i++)
			{
				var minimumDelta = targetChannel.Minimums[i] - currentChannel.Minimums[i];
				var maximumDelta = targetChannel.Maximums[i] - currentChannel.Maximums[i];
				var valueDelta = targetChannel.Values[i] - currentChannel.Values[i];
				minimums[i] = currentChannel.Minimums[i] + (minimumDelta * amount);
				maximums[i] = currentChannel.Maximums[i] + (maximumDelta * amount);
				values[i] = currentChannel.Values[i] + (valueDelta * amount);
				maxDelta = Math.Max(maxDelta, Math.Abs(minimumDelta));
				maxDelta = Math.Max(maxDelta, Math.Abs(maximumDelta));
				maxDelta = Math.Max(maxDelta, Math.Abs(valueDelta));
			}

			channels[channelIndex] = new WaveformChannelSnapshot(
				targetChannel.ChannelIndex,
				minimums,
				maximums,
				values,
				targetChannel.IsActive);
		}

		if (maxDelta <= settleThreshold)
		{
			settled = true;
			return target;
		}

		settled = false;
		return new WaveformSnapshot(channels, target.SourceFrameCount, target.SampleRate);
	}
}
