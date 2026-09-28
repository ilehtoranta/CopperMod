namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>
/// Explicitly unclaimed Layers boundary used by isolated graphics-core tests.
/// It carries no lock ownership or topology state.
/// </summary>
internal sealed class GraphicsUnboundLayerBackend : IGraphicsLayerBackend
{
    internal static GraphicsUnboundLayerBackend Instance { get; } = new();

    private GraphicsUnboundLayerBackend() { }

    public void Lock(uint layerAddress) => _ = layerAddress;
    public bool TryLock(uint layerAddress) => false;
    public void Unlock(uint layerAddress) => _ = layerAddress;
    public bool SyncSuperBitMap(uint layerAddress) => false;
    public bool CopySuperBitMap(uint layerAddress) => false;
}
