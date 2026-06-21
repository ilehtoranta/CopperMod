namespace CopperDisk;

internal sealed class ReadOnlyAdfDiskMedia : IAmigaSectorDiskMedia
{
    private readonly AdfDiskMedia _adf;

    public ReadOnlyAdfDiskMedia(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _adf = new AdfDiskMedia(data);
    }

    public int Cylinders => _adf.Cylinders;

    public int Heads => _adf.Heads;

    public bool HasCompleteDecodedSectorData => true;

    public ReadOnlyMemory<byte> SectorData => _adf.SectorData;

    public ReadOnlyMemory<byte> BootBlock => _adf.BootBlock;

    public IAmigaTrack ReadTrack(int cylinder, int head)
        => _adf.ReadTrack(cylinder, head);

    public ReadOnlyMemory<byte> ReadSector(int cylinder, int head, int sector)
        => _adf.ReadSector(cylinder, head, sector);

    public ReadOnlyMemory<byte> ReadSector(int logicalSector)
        => _adf.ReadSector(logicalSector);

    public ReadOnlyMemory<byte> ReadBytes(int byteOffset, int byteCount)
        => _adf.ReadBytes(byteOffset, byteCount);
}
