using CopperMod.Amiga;

namespace CopperScreen;

internal sealed record CopperScreenHardfileSettings(
	int Unit,
	string Path,
	bool ReadOnly,
	long CreateSizeBytes,
	AmigaHardfileMountMode Mode = AmigaHardfileMountMode.Auto,
	AmigaHardfilePartitionMetadata? Partition = null);
